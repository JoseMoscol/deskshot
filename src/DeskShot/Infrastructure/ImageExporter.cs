using DeskShot.Application;
using DeskShot.Domain;

namespace DeskShot.Infrastructure;

public static class AnnotationRenderer
{
    public static void Draw(DrawingContext dc, Annotation annotation, BitmapSource? original = null, BitmapSource? effectSource = null, Point effectOffset = default)
    {
        if (annotation.Points.Count < 2) return;
        Point first = annotation.Points[0], last = annotation.Points[^1];
        var brush = new SolidColorBrush(annotation.Color);
        var pen = new Pen(brush, annotation.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (RedactionProcessor.IsRedaction(annotation.Tool))
        {
            if (original is null) throw new ArgumentNullException(nameof(original));
            var bounds = RedactionProcessor.Bounds(annotation, original.PixelWidth, original.PixelHeight);
            if (bounds.Width == 0 || bounds.Height == 0) return;
            var rect = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            if (annotation.Tool == AnnotationTool.Redact)
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(annotation.Color.R, annotation.Color.G, annotation.Color.B)), null, rect);
            else
            {
                var source = effectSource ?? original;
                var translated = annotation with { Points = annotation.Points.Select(p => new Point(p.X + effectOffset.X, p.Y + effectOffset.Y)).ToArray() };
                var sourceBounds = RedactionProcessor.Bounds(translated, source.PixelWidth, source.PixelHeight);
                if (sourceBounds.Width > 0 && sourceBounds.Height > 0)
                    dc.DrawImage(RedactionProcessor.Filter(source, translated, sourceBounds),
                        new Rect(sourceBounds.X - effectOffset.X, sourceBounds.Y - effectOffset.Y, sourceBounds.Width, sourceBounds.Height));
            }
        }
        else if (annotation.Tool == AnnotationTool.Step)
        {
            Rect bounds = AnnotationGeometry.Bounds(annotation);
            double diameter = Math.Min(bounds.Width, bounds.Height);
            Point center = new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            dc.DrawEllipse(brush, new Pen(Brushes.White, Math.Max(1, diameter / 24)), center, diameter / 2, diameter / 2);
            var ink = annotation.Color.R * .299 + annotation.Color.G * .587 + annotation.Color.B * .114 > 155 ? Brushes.Black : Brushes.White;
            var text = new FormattedText(annotation.StepNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), diameter * .52, ink, 1);
            if (text.Width > diameter * .8) text.SetFontSize(diameter * .52 * diameter * .8 / text.Width);
            dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        }
        else if (annotation.Tool == AnnotationTool.Text)
        {
            Rect bounds = AnnotationGeometry.Bounds(annotation);
            dc.PushClip(new RectangleGeometry(bounds));
            dc.DrawText(AnnotationTextLayout.Format(annotation), bounds.TopLeft);
            dc.Pop();
        }
        else if (annotation.Tool == AnnotationTool.Rectangle)
            dc.DrawRectangle(null, pen, new Rect(first, last));
        else if (annotation.Tool == AnnotationTool.Highlight)
        {
            dc.PushOpacity(0.3);
            dc.DrawRectangle(brush, null, new Rect(first, last));
            dc.Pop();
        }
        else if (annotation.Tool == AnnotationTool.Arrow)
        {
            dc.DrawLine(pen, first, last);
            foreach (var point in AnnotationGeometry.ArrowHead(annotation)) dc.DrawLine(pen, last, point);
        }
        else
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(first, false, false);
                context.PolyLineTo(annotation.Points.Skip(1).ToArray(), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }
}

public sealed class ImageExporter : IImageExporter
{
    public static BitmapSource RenderForOcr(CaptureDocument document)
    {
        var redactions = document.Annotations.Where(a => RedactionProcessor.IsRedaction(a.Tool)).ToArray();
        if (redactions.Length == 0) return document.OriginalRegion();
        var safe = new CaptureDocument(document.Original) { EffectSource = document.EffectSource, EffectOffset = document.EffectOffset };
        safe.SetRegion(document.Region);
        foreach (var annotation in redactions) safe.Add(annotation);
        return new ImageExporter().Render(safe);
    }

    public BitmapSource Render(CaptureDocument document)
    {
        if (document.Annotations.Count == 0) return document.OriginalRegion();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(document.Original, new Rect(0, 0, document.Width, document.Height));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, document.Width, document.Height)));
            // Las zonas censuradas siempre cubren las anotaciones. Los bloques
            // sólidos se dibujan al final para no debilitarlos al solapar filtros.
            foreach (var annotation in document.Annotations.OrderBy(a => a.Tool == AnnotationTool.Redact ? 2 : RedactionProcessor.IsRedaction(a.Tool) ? 1 : 0))
                AnnotationRenderer.Draw(dc, annotation, document.Original, document.EffectSource, document.EffectOffset);
            dc.Pop();
        }
        // 96 DPI hace que una unidad de dibujo sea exactamente un píxel exportado.
        var rendered = new RenderTargetBitmap(document.Width, document.Height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        if (document.Region != new Int32Rect(0, 0, document.Width, document.Height))
        {
            var crop = new CroppedBitmap(rendered, document.Region);
            crop.Freeze();
            return crop;
        }
        return rendered;
    }

    public string Save(BitmapSource image, string folder, ImageFileFormat format, int jpegQuality)
    {
        Directory.CreateDirectory(folder);
        string extension = format == ImageFileFormat.Png ? ".png" : ".jpg";
        string path = Path.Combine(folder, $"DeskShot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{Guid.NewGuid().ToString("N")[..6]}{extension}");
        WriteAtomically(image, path, format, jpegQuality, overwrite: false);
        return path;
    }

    public void SaveTo(BitmapSource image, string path, ImageFileFormat format, int jpegQuality) =>
        WriteAtomically(image, path, format, jpegQuality, overwrite: true);

    private static void WriteAtomically(BitmapSource image, string path, ImageFileFormat format, int quality, bool overwrite)
    {
        if (!Enum.IsDefined(format)) throw new ArgumentException("Formato no válido.");
        if (quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(quality));
        string fullPath = Path.GetFullPath(path);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            BitmapEncoder encoder = format == ImageFileFormat.Png ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) encoder.Save(stream);
            File.Move(temporary, fullPath, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
