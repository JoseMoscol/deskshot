namespace DeskShot.Domain;

public enum ResizeHandle { TopLeft, TopRight, BottomRight, BottomLeft, Start, End }

public static class AnnotationGeometry
{
    public static Rect Bounds(Annotation annotation)
    {
        double x = annotation.Points.Min(p => p.X), y = annotation.Points.Min(p => p.Y);
        double right = annotation.Points.Max(p => p.X), bottom = annotation.Points.Max(p => p.Y);
        // Mantener tiradores utilizables para líneas horizontales, verticales y puntos.
        return new Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    public static Point[] ArrowHead(Annotation annotation)
    {
        Point first = annotation.Points[0], last = annotation.Points[^1];
        Vector direction = first - last;
        if (direction.Length < 1) return [last, last];
        double length = Math.Min(Math.Max(annotation.Width * 4, 12), direction.Length * 0.45);
        direction.Normalize();
        Vector side = new(-direction.Y, direction.X);
        return [last + direction * length + side * length * 0.5, last + direction * length - side * length * 0.5];
    }

    public static IReadOnlyList<(ResizeHandle Handle, Point Point)> Handles(Annotation annotation)
    {
        if (annotation.Tool == AnnotationTool.Arrow)
            return [(ResizeHandle.Start, annotation.Points[0]), (ResizeHandle.End, annotation.Points[^1])];
        Rect b = Bounds(annotation);
        return [(ResizeHandle.TopLeft, b.TopLeft), (ResizeHandle.TopRight, b.TopRight),
            (ResizeHandle.BottomRight, b.BottomRight), (ResizeHandle.BottomLeft, b.BottomLeft)];
    }

    public static ResizeHandle? HitHandle(Annotation annotation, Point point, double tolerance)
    {
        // A zoom bajo pueden solaparse las áreas de clic: gana el tirador más cercano.
        return Handles(annotation)
            .Where(item => Math.Abs(point.X - item.Point.X) <= tolerance && Math.Abs(point.Y - item.Point.Y) <= tolerance)
            .OrderBy(item => (point - item.Point).LengthSquared)
            .Select(item => (ResizeHandle?)item.Handle).FirstOrDefault();
    }

    public static bool HitTest(Annotation annotation, Point point, double tolerance)
    {
        double radius = tolerance + annotation.Width / 2;
        if (annotation.Tool is AnnotationTool.Text or AnnotationTool.Rectangle or AnnotationTool.Highlight)
        {
            Rect bounds = Bounds(annotation);
            bounds.Inflate(tolerance, tolerance);
            return bounds.Contains(point);
        }
        for (int i = 1; i < annotation.Points.Count; i++)
            if (DistanceToSegment(point, annotation.Points[i - 1], annotation.Points[i]) <= radius) return true;
        if (annotation.Tool == AnnotationTool.Arrow)
            return ArrowHead(annotation).Any(p => DistanceToSegment(point, annotation.Points[^1], p) <= radius);
        return false;
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        Vector line = end - start;
        if (line.LengthSquared < 0.000001) return (point - start).Length;
        double t = Math.Clamp(Vector.Multiply(point - start, line) / line.LengthSquared, 0, 1);
        return (point - (start + line * t)).Length;
    }

    public static Annotation Move(Annotation annotation, Vector delta, Size canvas)
    {
        Rect bounds = Bounds(annotation);
        double dx = Math.Clamp(delta.X, -bounds.Left, Math.Max(-bounds.Left, canvas.Width - bounds.Right));
        double dy = Math.Clamp(delta.Y, -bounds.Top, Math.Max(-bounds.Top, canvas.Height - bounds.Bottom));
        return annotation with { Points = annotation.Points.Select(p => new Point(p.X + dx, p.Y + dy)).ToArray() };
    }

    public static Annotation Resize(Annotation annotation, ResizeHandle handle, Point position, Size canvas)
    {
        var point = new Point(Math.Clamp(position.X, 0, canvas.Width), Math.Clamp(position.Y, 0, canvas.Height));
        if (annotation.Tool == AnnotationTool.Arrow && handle is ResizeHandle.Start or ResizeHandle.End)
        {
            var points = annotation.Points.ToArray();
            points[handle == ResizeHandle.Start ? 0 : points.Length - 1] = point;
            return annotation with { Points = points };
        }
        Rect source = Bounds(annotation);
        bool left = handle is ResizeHandle.TopLeft or ResizeHandle.BottomLeft;
        bool top = handle is ResizeHandle.TopLeft or ResizeHandle.TopRight;
        var anchor = new Point(left ? source.Right : source.Left, top ? source.Bottom : source.Top);
        double availableWidth = Math.Max(1, left ? anchor.X : canvas.Width - anchor.X);
        double availableHeight = Math.Max(1, top ? anchor.Y : canvas.Height - anchor.Y);
        double width = Math.Clamp(left ? anchor.X - point.X : point.X - anchor.X, Math.Min(4, availableWidth), availableWidth);
        double height = Math.Clamp(top ? anchor.Y - point.Y : point.Y - anchor.Y, Math.Min(4, availableHeight), availableHeight);
        double sx = width / source.Width, sy = height / source.Height;
        double fontSize = annotation.FontSize;
        if (annotation.Tool == AnnotationTool.Text)
        {
            // El texto mantiene sus proporciones y escala la tipografía, sin deformarla.
            double minimum = 4 / annotation.FontSize;
            double maximum = Math.Min(4096 / annotation.FontSize, Math.Min(availableWidth / source.Width, availableHeight / source.Height));
            double scale = Math.Clamp(Math.Min(sx, sy), Math.Min(minimum, maximum), maximum);
            sx = sy = scale;
            width = source.Width * scale;
            height = source.Height * scale;
            fontSize *= scale;
        }
        var origin = new Point(left ? anchor.X - width : anchor.X, top ? anchor.Y - height : anchor.Y);
        return annotation with
        {
            Points = annotation.Points.Select(p => new Point(origin.X + (p.X - source.X) * sx, origin.Y + (p.Y - source.Y) * sy)).ToArray(),
            FontSize = fontSize
        };
    }
}
