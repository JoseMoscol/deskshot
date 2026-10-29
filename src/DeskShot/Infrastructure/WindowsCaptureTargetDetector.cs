using System.Windows.Automation;
using DeskShot.Application;
using DeskShot.Domain;

namespace DeskShot.Infrastructure;

// La lista nativa se congela antes de mostrar el selector. La consulta de controles parte
// del HWND original, no de FromPoint, que devolvería nuestra superposición de pantalla.
public sealed class WindowsCaptureTargetDetector : ICaptureTargetDetector
{
    private sealed record TargetWindow(nint Handle, PixelBounds Bounds, CaptureTarget Target, CaptureTarget? Client);
    private readonly PixelBounds _desktop;
    private readonly List<TargetWindow> _windows = [];
    private static readonly SemaphoreSlim Pending = new(1);
    private Task<IReadOnlyList<CaptureTarget>>? _query;
    private Point _queryPoint;
    private int _queryWindow;

    public WindowsCaptureTargetDetector(PixelBounds desktop)
    {
        _desktop = desktop;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (_windows.Count >= 256) return false;
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == Environment.ProcessId) return true;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var bounds = Bounds(hwnd);
            if (bounds is not { Width: >= 2, Height: >= 2 } physical || CaptureTargetGeometry.Clip(physical, desktop) is not { } region) return true;
            int id = _windows.Count + 1;
            CaptureTarget? client = null;
            if (NativeMethods.GetClientRect(hwnd, out var cr))
            {
                var origin = new NativeMethods.NativePoint();
                if (NativeMethods.ClientToScreen(hwnd, ref origin) && CaptureTargetGeometry.Clip(new(origin.X, origin.Y, cr.Right, cr.Bottom), desktop) is { } clientRegion && clientRegion != region)
                    client = new(clientRegion, "Área de la aplicación", id);
            }
            _windows.Add(new(hwnd, physical, new(region, "Ventana", id), client));
            return true;
        }, 0);
    }
    private static PixelBounds? Bounds(nint hwnd)
    {
        if (NativeMethods.DwmGetWindowAttribute(hwnd, 9, out NativeMethods.NativeRect rect, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NativeRect>()) != 0 && !NativeMethods.GetWindowRect(hwnd, out rect)) return null;
        return new(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
    }
    private TargetWindow? Find(Point point) => _windows.FirstOrDefault(w => CaptureTargetGeometry.Contains(w.Target.Region, point));
    public CaptureTarget? WindowAt(Point imagePoint) => Find(imagePoint)?.Target;

    public async Task<IReadOnlyList<CaptureTarget>> QueryAsync(Point imagePoint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var window = Find(imagePoint);
        if (window is null) return [];
        IReadOnlyList<CaptureTarget> fallback = CaptureTargetGeometry.Order(new[] { window.Client, window.Target }.OfType<CaptureTarget>(), imagePoint);
        bool same = _queryWindow == window.Target.WindowId && (_queryPoint - imagePoint).Length <= 3;
        if (same && _query?.IsCompletedSuccessfully == true) return _query.Result;
        if (_query is { IsCompleted: false } || !Pending.Wait(0)) return fallback;
        _queryPoint = imagePoint; _queryWindow = window.Target.WindowId;
        _query = Task.Run<IReadOnlyList<CaptureTarget>>(() =>
        {
            try
            {
                if (Bounds(window.Handle) != window.Bounds) return fallback;
                var found = fallback.ToList();
                int nativeCount = 0;
                NativeMethods.EnumChildWindows(window.Handle, (child, _) =>
                {
                    if (++nativeCount > 256) return false;
                    if (NativeMethods.IsWindowVisible(child) && Bounds(child) is { } bounds) Add(bounds, "Control de la aplicación");
                    return true;
                }, 0);
                var root = AutomationElement.FromHandle(window.Handle);
                int visited = 0;
                void Walk(AutomationElement element, int depth)
                {
                    if (++visited > 512 || depth > 24) return;
                    var current = element.Current;
                    if (current.IsOffscreen) return;
                    var rect = current.BoundingRectangle;
                    var physicalPoint = new Point(_desktop.X + imagePoint.X, _desktop.Y + imagePoint.Y);
                    if (!rect.IsEmpty && rect.Width > 0 && !rect.Contains(physicalPoint)) return;
                    var type = current.ControlType;
                    if (type == ControlType.Pane || type == ControlType.Document || type == ControlType.Group || type == ControlType.Edit || type == ControlType.List || type == ControlType.Tree || type == ControlType.DataGrid)
                        if (!rect.IsEmpty && rect.Width >= 12 && rect.Height >= 12 && rect.Width < int.MaxValue && rect.Height < int.MaxValue)
                        {
                            int x = (int)Math.Ceiling(rect.Left), y = (int)Math.Ceiling(rect.Top);
                            Add(new(x, y, (int)Math.Floor(rect.Right) - x, (int)Math.Floor(rect.Bottom) - y), type == ControlType.Document ? "Documento" : type == ControlType.Edit ? "Campo de texto" : "Panel");
                        }
                    var walker = TreeWalker.ControlViewWalker;
                    for (var child = walker.GetFirstChild(element); child is not null && visited < 512; child = walker.GetNextSibling(child)) Walk(child, depth + 1);
                }
                void Add(PixelBounds bounds, string label)
                {
                    long left = Math.Max(bounds.X, window.Bounds.X), top = Math.Max(bounds.Y, window.Bounds.Y);
                    long right = Math.Min((long)bounds.X + bounds.Width, (long)window.Bounds.X + window.Bounds.Width);
                    long bottom = Math.Min((long)bounds.Y + bounds.Height, (long)window.Bounds.Y + window.Bounds.Height);
                    if (right - left < 12 || bottom - top < 12) return;
                    if (CaptureTargetGeometry.Clip(new((int)left, (int)top, (int)(right - left), (int)(bottom - top)), _desktop) is { } region && CaptureTargetGeometry.Contains(region, imagePoint))
                        found.Add(new(region, label, window.Target.WindowId));
                }
                try { Walk(root, 0); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
                return CaptureTargetGeometry.Order(found, imagePoint);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return fallback; }
            finally { Pending.Release(); }
        });
        var completed = await Task.WhenAny(_query, Task.Delay(220, token));
        token.ThrowIfCancellationRequested();
        return completed == _query ? await _query : fallback;
    }
}
