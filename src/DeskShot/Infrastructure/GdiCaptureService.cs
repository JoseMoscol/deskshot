using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using DeskShot.Application;
using DeskShot.Domain;

namespace DeskShot.Infrastructure;

public sealed class GdiCaptureService : ICaptureService
{
    public DesktopFrame CaptureDesktop()
    {
        // SM_*VIRTUALSCREEN devuelve píxeles físicos gracias a PerMonitorV2.
        var bounds = new PixelBounds(NativeMethods.GetSystemMetrics(76), NativeMethods.GetSystemMetrics(77),
            NativeMethods.GetSystemMetrics(78), NativeMethods.GetSystemMetrics(79));
        return Capture(bounds);
    }

    public BitmapSource CaptureRegion(PixelBounds bounds) => Capture(bounds).Image;

    private static DesktopFrame Capture(PixelBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidOperationException("No se encontró una pantalla disponible.");
        nint desktop = NativeMethods.GetDC(0), memory = 0, bitmap = 0, previous = 0;
        if (desktop == 0) throw new Win32Exception("No se pudo acceder a la pantalla.");
        try
        {
            memory = NativeMethods.CreateCompatibleDC(desktop);
            if (memory == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            bitmap = NativeMethods.CreateCompatibleBitmap(desktop, bounds.Width, bounds.Height);
            if (bitmap == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            previous = NativeMethods.SelectObject(memory, bitmap);
            if (previous == 0 || previous == -1) throw new Win32Exception("No se pudo preparar la captura.");
            if (!NativeMethods.BitBlt(memory, 0, 0, bounds.Width, bounds.Height, desktop, bounds.X, bounds.Y, 0x00CC0020 | 0x40000000))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // GDI no garantiza el canal alfa; Bgr32 evita exportaciones transparentes.
            var opaque = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
            opaque.Freeze();
            var monitors = new List<PixelBounds>();
            NativeMethods.EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref NativeMethods.NativeRect rect, nint data) =>
            {
                monitors.Add(new PixelBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
                return true;
            }, 0);
            return new DesktopFrame(opaque, bounds, monitors.AsReadOnly());
        }
        finally
        {
            if (previous != 0 && previous != -1 && memory != 0) NativeMethods.SelectObject(memory, previous);
            if (bitmap != 0) NativeMethods.DeleteObject(bitmap);
            if (memory != 0) NativeMethods.DeleteDC(memory);
            NativeMethods.ReleaseDC(0, desktop);
        }
    }
}
