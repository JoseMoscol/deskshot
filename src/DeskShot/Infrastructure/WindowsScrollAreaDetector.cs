using System.Windows.Automation;
using DeskShot.Domain;

namespace DeskShot.Infrastructure;

public sealed record ScrollAreaSuggestion(PixelBounds Bounds, bool Detected, string Message);

public static class WindowsScrollAreaDetector
{
    private static readonly SemaphoreSlim Pending = new(1);
    public static async Task<ScrollAreaSuggestion> DetectAsync(PixelBounds selection, CancellationToken token)
    {
        var fallback = new ScrollAreaSuggestion(selection, false, "La ventana no expone un área desplazable. Se usa tu selección; se intentarán excluir los bordes fijos por imagen.");
        if (!Pending.Wait(0)) return fallback;
        var point = new Point(selection.X + selection.Width / 2, selection.Y + selection.Height / 2);
        var work = Task.Run(() =>
        {
            try
            {
                var element = AutomationElement.FromPoint(point);
                for (int depth = 0; element is not null && depth < 24; depth++, element = TreeWalker.ControlViewWalker.GetParent(element))
                {
                    if (element.Current.ProcessId == Environment.ProcessId) return fallback;
                    if (element.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern) && ((ScrollPattern)pattern).Current.VerticallyScrollable)
                    {
                        Rect area = element.Current.BoundingRectangle;
                        // Las coordenadas de UI Automation son píxeles físicos. Limitar al escritorio visible.
                        area.Intersect(new Rect(NativeMethods.GetSystemMetrics(76), NativeMethods.GetSystemMetrics(77), NativeMethods.GetSystemMetrics(78), NativeMethods.GetSystemMetrics(79)));
                        if (area.IsEmpty || !area.Contains(point) || area.Width < 64 || area.Height < 100 || area.Width * area.Height > Application.ScrollStitcher.DefaultMaxPixels) continue;
                        var bars = element.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ScrollBar));
                        for (int i = 0; i < Math.Min(32, bars.Count); i++)
                        {
                            Rect bar = bars[i].Current.BoundingRectangle;
                            if (bar.Height > bar.Width && Math.Abs(bar.Right - area.Right) < 3 && bar.Height >= area.Height / 2) area.Width = Math.Max(0, bar.Left - area.Left);
                            else if (bar.Width > bar.Height && Math.Abs(bar.Bottom - area.Bottom) < 3 && bar.Width >= area.Width / 2) area.Height = Math.Max(0, bar.Top - area.Top);
                        }
                        int x = (int)Math.Ceiling(area.Left), y = (int)Math.Ceiling(area.Top);
                        int width = (int)Math.Floor(area.Right) - x, height = (int)Math.Floor(area.Bottom) - y;
                        if (width >= 64 && height >= 100)
                            return new ScrollAreaSuggestion(new(x, y, width, height), true, $"Área desplazable detectada: {width} × {height} px. Se buscarán encabezados y bordes fijos al iniciar el recorrido.");
                    }
                    if (element.Current.ControlType == ControlType.Window) break;
                }
                return fallback;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return fallback; }
            finally { Pending.Release(); }
        });
        // Un proveedor de accesibilidad bloqueado no inmoviliza la interfaz ni crea más consultas.
        var completed = await Task.WhenAny(work, Task.Delay(1800, token));
        token.ThrowIfCancellationRequested();
        return completed == work ? await work : fallback;
    }
}
