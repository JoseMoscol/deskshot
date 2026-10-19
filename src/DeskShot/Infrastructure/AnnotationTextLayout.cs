using System.Globalization;
using DeskShot.Domain;

namespace DeskShot.Infrastructure;

public static class AnnotationTextLayout
{
    private static FormattedText Format(string text, double size, bool bold, Color color, double maxWidth)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
            bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, size, new SolidColorBrush(color), 1) { MaxTextWidth = Math.Max(1, maxWidth) };
    }

    public static FormattedText Format(Annotation annotation) => Format(annotation.Text, annotation.FontSize,
        annotation.IsBold, annotation.Color, AnnotationGeometry.Bounds(annotation).Width);

    public static Annotation Create(string text, Point position, Color color, double size, bool bold, Size canvas, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 5000)
            throw new ArgumentException("Escribe un texto de hasta 5000 caracteres.");
        if (!double.IsFinite(size) || size is < 4 or > 4096)
            throw new ArgumentException("El tamaño del texto debe estar entre 4 y 4096 píxeles.");
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        double x = Math.Clamp(position.X, 0, Math.Max(0, canvas.Width - Math.Min(120, canvas.Width)));
        var formatted = Format(text, size, bold, color, canvas.Width - x);
        double width = Math.Min(canvas.Width - x, Math.Max(1, Math.Ceiling(formatted.WidthIncludingTrailingWhitespace + 2)));
        formatted.MaxTextWidth = width;
        double height = Math.Ceiling(formatted.Height + 2);
        if (height > canvas.Height)
            throw new ArgumentException("El texto no cabe en la captura. Reduce el tamaño o usa menos líneas.");
        double y = Math.Clamp(position.Y, 0, Math.Max(0, canvas.Height - height));
        return new Annotation(AnnotationTool.Text, color, 1, new[] { new Point(x, y), new Point(x + width, y + height) })
        { Id = id ?? Guid.NewGuid(), Text = text, FontSize = size, IsBold = bold };
    }
}
