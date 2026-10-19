using DeskShot.Application;
using DeskShot.Domain;

namespace DeskShot.Infrastructure;

public static class AnnotationRenderer
{
    public static void Draw(DrawingContext dc, Annotation annotation)
    {
        if (annotation.Points.Count < 2) return;
        Point first = annotation.Points[0], last = annotation.Points[^1];
        var brush = new SolidColorBrush(annotation.Color);
        var pen = new Pen(brush, annotation.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (annotation.Tool == AnnotationTool.Text)
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
    public BitmapSource Render(CaptureDocument document)
    {
        if (document.Annotations.Count == 0) return document.OriginalRegion();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(document.Original, new Rect(0, 0, document.Width, document.Height));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, document.Width, document.Height)));
            foreach (var annotation in document.Annotations) AnnotationRenderer.Draw(dc, annotation);
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
