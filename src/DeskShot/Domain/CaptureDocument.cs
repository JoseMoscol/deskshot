namespace DeskShot.Domain;

public enum AnnotationTool { Pen, Arrow, Rectangle, Highlight, Text, Redact, Pixelate, Blur, Step }

// Las coordenadas y el grosor siempre se guardan en píxeles de la imagen original.
public sealed record Annotation(AnnotationTool Tool, Color Color, double Width, IReadOnlyList<Point> Points)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Text { get; init; } = "";
    public double FontSize { get; init; } = 32;
    public bool IsBold { get; init; }
    public int EffectSize { get; init; } = 12;
    public int StepNumber { get; init; } = 1;
}

public sealed class CaptureDocument
{
    private List<Annotation> _annotations = [];
    private sealed record State(Annotation[] Annotations, Int32Rect Region, long Revision);
    private readonly Stack<State> _undo = [];
    private readonly Stack<State> _redo = [];
    private long _nextRevision;
    public BitmapSource Original { get; }
    internal BitmapSource EffectSource { get; init; }
    internal Point EffectOffset { get; init; }
    public IReadOnlyList<Annotation> Annotations => _annotations.AsReadOnly();
    public DateTime CreatedAt { get; } = DateTime.Now;
    public int Width => Original.PixelWidth;
    public int Height => Original.PixelHeight;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public long Revision { get; private set; }
    public int NextStepNumber => _annotations.Where(a => a.Tool == AnnotationTool.Step).Select(a => a.StepNumber).DefaultIfEmpty(0).Max() + 1;
    public Int32Rect Region { get; private set; }
    public event EventHandler? Changed;

    public CaptureDocument(BitmapSource original)
    {
        ArgumentNullException.ThrowIfNull(original);
        // Aislamos el documento de cambios externos en un WriteableBitmap.
        Original = original.Clone();
        Original.Freeze();
        EffectSource = Original;
        Region = new Int32Rect(0, 0, Width, Height);
    }

    public void SetRegion(Int32Rect region)
    {
        if (region.Width < 2 || region.Height < 2 || region.X < 0 || region.Y < 0 ||
            (long)region.X + region.Width > Width || (long)region.Y + region.Height > Height)
            throw new ArgumentException("Selecciona un área de al menos 2 × 2 píxeles dentro del escritorio.");
        if (region == Region) return;
        Commit(() => Region = region);
    }

    public BitmapSource OriginalRegion()
    {
        if (Region == new Int32Rect(0, 0, Width, Height)) return Original;
        var image = new CroppedBitmap(Original, Region);
        image.Freeze();
        return image;
    }

    public CaptureDocument CreateRegionDocument()
    {
        var result = new CaptureDocument(OriginalRegion()) { EffectSource = EffectSource, EffectOffset = new Point(EffectOffset.X + Region.X, EffectOffset.Y + Region.Y) };
        var crop = new Rect(Region.X, Region.Y, Region.Width, Region.Height);
        foreach (var annotation in Annotations)
        {
            Rect bounds = AnnotationGeometry.Bounds(annotation);
            if (annotation.Tool == AnnotationTool.Arrow)
                foreach (var point in AnnotationGeometry.ArrowHead(annotation)) bounds.Union(point);
            bounds.Inflate(annotation.Width / 2, annotation.Width / 2);
            if (!bounds.IntersectsWith(crop)) continue;
            result.Add(annotation with { Points = annotation.Points.Select(p => new Point(p.X - Region.X, p.Y - Region.Y)).ToArray() });
        }
        return result;
    }

    public void Add(Annotation annotation)
    {
        if (annotation.Points.Count < 2) return;
        var frozen = Freeze(annotation);
        if (_annotations.Any(a => a.Id == frozen.Id)) throw new ArgumentException("La anotación ya existe.");
        Commit(() => _annotations.Add(frozen));
    }

    public Annotation? Find(Guid? id) => id is null ? null : _annotations.FirstOrDefault(a => a.Id == id);

    public void Replace(Guid id, Annotation replacement)
    {
        int index = _annotations.FindIndex(a => a.Id == id);
        if (index < 0) return;
        var frozen = Freeze(replacement with { Id = id });
        var current = _annotations[index];
        if (current.Tool == frozen.Tool && current.Color == frozen.Color && current.Width == frozen.Width &&
            current.Text == frozen.Text && current.FontSize == frozen.FontSize && current.IsBold == frozen.IsBold &&
            current.EffectSize == frozen.EffectSize && current.StepNumber == frozen.StepNumber &&
            current.Points.SequenceEqual(frozen.Points)) return;
        Commit(() => _annotations[index] = frozen);
    }

    public void Remove(Guid id)
    {
        int index = _annotations.FindIndex(a => a.Id == id);
        if (index >= 0) Commit(() => _annotations.RemoveAt(index));
    }

    private static Annotation Freeze(Annotation annotation)
    {
        if (!Enum.IsDefined(annotation.Tool) || annotation.Points.Count < 2 ||
            !double.IsFinite(annotation.Width) || annotation.Width <= 0 ||
            annotation.Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            throw new ArgumentException("La anotación contiene coordenadas o grosores inválidos.");
        if (annotation.Tool == AnnotationTool.Text && (string.IsNullOrWhiteSpace(annotation.Text) || annotation.Text.Length > 5000 ||
            !double.IsFinite(annotation.FontSize) || annotation.FontSize < 4 || annotation.FontSize > 4096))
            throw new ArgumentException("Introduce texto de hasta 5000 caracteres y un tamaño entre 4 y 4096 píxeles.");
        if (annotation.EffectSize is < 2 or > 64 || annotation.StepNumber is < 1 or > 9999)
            throw new ArgumentException("La intensidad debe estar entre 2 y 64 y el número entre 1 y 9999.");
        return annotation with { Points = Array.AsReadOnly(annotation.Points.ToArray()) };
    }

    private State Snapshot() => new(_annotations.ToArray(), Region, Revision);
    private void Commit(Action change)
    {
        _undo.Push(Snapshot());
        _redo.Clear();
        change();
        Revision = ++_nextRevision;
        OnChanged();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        _redo.Push(Snapshot());
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _undo.Push(Snapshot());
        Restore(_redo.Pop());
    }

    private void Restore(State state)
    {
        _annotations = state.Annotations.ToList();
        Region = state.Region;
        Revision = state.Revision;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

public readonly record struct PixelBounds(int X, int Y, int Width, int Height);
public sealed record DesktopFrame(BitmapSource Image, PixelBounds Bounds, IReadOnlyList<PixelBounds>? Monitors = null);

public static class SelectionMath
{
    public static Int32Rect FromPoints(Point start, Point end, int width, int height)
    {
        int left = (int)Math.Floor(Math.Clamp(Math.Min(start.X, end.X), 0, width));
        int top = (int)Math.Floor(Math.Clamp(Math.Min(start.Y, end.Y), 0, height));
        int right = (int)Math.Ceiling(Math.Clamp(Math.Max(start.X, end.X), 0, width));
        int bottom = (int)Math.Ceiling(Math.Clamp(Math.Max(start.Y, end.Y), 0, height));
        return new Int32Rect(left, top, right - left, bottom - top);
    }
}
