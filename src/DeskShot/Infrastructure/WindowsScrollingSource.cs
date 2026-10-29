using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskShot.Application;
using DeskShot.Domain;

namespace DeskShot.Infrastructure;

public sealed class WindowsScrollingSource : IScrollingCaptureSource
{
    private readonly PixelBounds _bounds;
    private readonly int _settleMilliseconds;
    private readonly Action<bool> _showControls;
    private readonly GdiCaptureService _capture = new();
    private nint _target;
    private NativeMethods.NativePoint _scrollPoint;

    public WindowsScrollingSource(PixelBounds bounds, int settleMilliseconds, Action<bool> showControls)
    {
        _bounds = bounds; _settleMilliseconds = settleMilliseconds; _showControls = showControls;
        _scrollPoint = new() { X = bounds.X + bounds.Width / 2, Y = bounds.Y + bounds.Height / 2 };
    }

    public void Prepare()
    {
        _target = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(_scrollPoint), 2);
        if (_target == 0) throw new InvalidOperationException("No se encontró la ventana bajo el área seleccionada.");
        NativeMethods.SetForegroundWindow(_target);
        if (NativeMethods.GetForegroundWindow() != _target)
            throw new InvalidOperationException("No se pudo activar el documento. Actívalo antes de iniciar la captura.");
        NativeMethods.SetCursorPos(_bounds.X + _bounds.Width / 2, _bounds.Y - 1);
    }

    public async Task ScrollDownAsync(CancellationToken token)
    {
        Check(token);
        _showControls(false);
        await WaitAsync(80, token);
        if (NativeMethods.GetForegroundWindow() != _target)
            throw new InvalidOperationException("Cambió la ventana activa.");
        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x01, 0x02 }.Any(k => NativeMethods.GetAsyncKeyState(k) < 0))
            throw new InvalidOperationException("Se pulsó una tecla modificadora o un botón del ratón.");
        if (!NativeMethods.SetCursorPos(_scrollPoint.X, _scrollPoint.Y))
            throw new InvalidOperationException("No se pudo colocar el puntero en el documento.");
        if (NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(_scrollPoint), 2) != _target)
            throw new InvalidOperationException("Otra ventana está cubriendo el documento.");
        var input = new NativeMethods.Input { Union = new() { Mouse = new() { Data = unchecked((uint)-120), Flags = 0x0800 } } };
        if (NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.Input>()) != 1)
            throw new InvalidOperationException("Windows bloqueó el desplazamiento. Comprueba que el documento no se ejecuta como administrador.");
        NativeMethods.SetCursorPos(_bounds.X + _bounds.Width / 2, _bounds.Y - 1);
        _showControls(true);
        await WaitAsync(_settleMilliseconds, token);
    }

    public async Task<BitmapSource> CaptureAsync(CancellationToken token)
    {
        Check(token); _showControls(false);
        try
        {
            await WaitAsync(100, token); NativeMethods.DwmFlush();
            var watch = Stopwatch.StartNew();
            var previous = await Task.Run(() => _capture.CaptureRegion(_bounds), token);
            byte[] bytes = Pixels(previous); int stable = 0;
            while (watch.ElapsedMilliseconds < 4000)
            {
                await WaitAsync(180, token); NativeMethods.DwmFlush();
                var current = await Task.Run(() => _capture.CaptureRegion(_bounds), token);
                var nextBytes = Pixels(current);
                stable = bytes.AsSpan().SequenceEqual(nextBytes) ? stable + 1 : 0;
                if (stable >= 2) return current;
                bytes = nextBytes;
            }
            throw new InvalidOperationException("El contenido no se estabiliza. Espera a que termine de cargar o excluye las animaciones.");
        }
        finally { _showControls(true); }
    }
    private static byte[] Pixels(BitmapSource image)
    {
        byte[] pixels = new byte[checked(image.PixelWidth * image.PixelHeight * 4)];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels;
    }
    private static void Check(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (NativeMethods.GetAsyncKeyState(0x1B) < 0) throw new OperationCanceledException("Terminaste con Esc.");
    }
    private static async Task WaitAsync(int milliseconds, CancellationToken token)
    {
        for (int remaining = milliseconds; remaining > 0; remaining -= 50)
        { Check(token); await Task.Delay(Math.Min(50, remaining), token); }
        Check(token);
    }
}
