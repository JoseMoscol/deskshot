using DeskShot.Domain;

namespace DeskShot.Application;

/// <summary>Selección y gestos en píxeles originales; cada gesto confirma una sola edición.</summary>
public sealed class AnnotationEditor : IDisposable
{
    private readonly CaptureDocument _document;
    private Annotation? _original;
    private Point _start;
    private ResizeHandle? _handle;
    public Guid? SelectedId { get; private set; }
    public Annotation? Selected => _document.Find(SelectedId);
    public Annotation? Preview { get; private set; }
    public bool IsDragging => _original is not null;
    public event EventHandler? Changed;
    public event EventHandler? SelectionChanged;

    public AnnotationEditor(CaptureDocument document)
    {
        _document = document;
        _document.Changed += OnDocumentChanged;
    }

    public void Select(Guid? id)
    {
        Cancel();
        SelectedId = _document.Find(id)?.Id;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Annotation? HitTest(Point point, double tolerance) =>
        _document.Annotations.OrderBy(a => a.Tool == AnnotationTool.Redact ? 2 : a.Tool is AnnotationTool.Pixelate or AnnotationTool.Blur ? 1 : 0)
            .Reverse().FirstOrDefault(a => AnnotationGeometry.HitTest(a, point, tolerance));

    public bool Begin(Point point, double tolerance)
    {
        Cancel();
        _handle = Selected is { } selected ? AnnotationGeometry.HitHandle(selected, point, tolerance) : null;
        if (_handle is null)
        {
            SelectedId = HitTest(point, tolerance)?.Id;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        _original = Selected;
        _start = point;
        Preview = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return _original is not null;
    }

    public void Drag(Point point)
    {
        if (_original is null) return;
        if ((point - _start).Length < 0.01) { Preview = null; Changed?.Invoke(this, EventArgs.Empty); return; }
        var canvas = new Size(_document.Width, _document.Height);
        Preview = _handle is { } handle ? AnnotationGeometry.Resize(_original, handle, point, canvas)
            : AnnotationGeometry.Move(_original, point - _start, canvas);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Commit()
    {
        var preview = Preview;
        ResetGesture();
        if (preview is not null) _document.Replace(preview.Id, preview);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        ResetGesture();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ResetGesture() { _original = null; Preview = null; _handle = null; }
    public void UpdateSelected(Annotation replacement)
    {
        Cancel();
        if (SelectedId is { } id) _document.Replace(id, replacement);
    }
    public void DeleteSelected()
    {
        Cancel();
        if (SelectedId is { } id) _document.Remove(id);
    }
    public void Nudge(Vector delta)
    {
        if (Selected is { } selected)
            UpdateSelected(AnnotationGeometry.Move(selected, delta, new Size(_document.Width, _document.Height)));
    }
    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        ResetGesture();
        if (Selected is null) SelectedId = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() => _document.Changed -= OnDocumentChanged;
}
