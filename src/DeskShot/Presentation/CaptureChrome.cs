using System.Globalization;
using DeskShot.Domain;
using DeskShot.Application;

namespace DeskShot.Presentation;

public sealed class CaptureChrome(int pixelWidth, int pixelHeight) : FrameworkElement
{
    public Int32Rect? Region { get; set; }
    public CaptureTarget? Target { get; set; }
    public string Hint { get; set; } = "Arrastra para seleccionar · Esc para cancelar";
    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var desktop = new Rect(0, 0, ActualWidth, ActualHeight);
        var dark = new SolidColorBrush(Color.FromArgb(135, 0, 0, 0));
        var shown = Region ?? Target?.Region;
        if (shown is not { Width: >= 2, Height: >= 2 } region)
        {
            dc.DrawRectangle(dark, null, desktop);
            Label(dc, Hint, Math.Max(12, (ActualWidth - 570) / 2), 32);
            return;
        }
        double sx = ActualWidth / pixelWidth, sy = ActualHeight / pixelHeight;
        var selection = new Rect(region.X * sx, region.Y * sy, region.Width * sx, region.Height * sy);
        dc.DrawGeometry(dark, null, new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(desktop), new RectangleGeometry(selection)));
        var accent = new SolidColorBrush(Color.FromRgb(120, 231, 195));
        dc.DrawRectangle(null, new Pen(Brushes.Black, 3), selection);
        dc.DrawRectangle(null, new Pen(accent, 1) { DashStyle = DashStyles.Dash }, selection);
        if (Region is not null) foreach (var handle in RegionGeometry.Handles(region))
        {
            Point point = new(handle.Point.X * sx, handle.Point.Y * sy);
            dc.DrawRectangle(Brushes.White, new Pen(accent, 1.5), new Rect(point.X - 3.5, point.Y - 3.5, 7, 7));
        }
        Label(dc, Target is not null && Region is null ? $"{Target.Label} · {region.Width} × {region.Height} px · clic para elegir" : $"{region.Width} × {region.Height} px", selection.Left, selection.Top - 34);
        if (Region is null) Label(dc, Hint, Math.Max(12, (ActualWidth - 650) / 2), ActualHeight - 48);
    }

    private void Label(DrawingContext dc, string message, double x, double y)
    {
        var text = new FormattedText(message, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        x = Math.Clamp(x, 8, Math.Max(8, ActualWidth - text.Width - 24));
        y = Math.Clamp(y, 8, Math.Max(8, ActualHeight - 32));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(23, 31, 43)), null,
            new Rect(x, y, text.Width + 18, 27), 5, 5);
        dc.DrawText(text, new Point(x + 9, y + 4));
    }
}
