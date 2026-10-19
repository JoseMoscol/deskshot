using DeskShot.Application;
using DeskShot.Domain;
using DeskShot.Infrastructure;

namespace DeskShot.Presentation;

public enum EditorTool { Select, Pen, Arrow, Rectangle, Highlight, Text }
public sealed class AnnotationTextRequestedEventArgs(Point position, Annotation? existing = null) : EventArgs
{
    public Point Position { get; } = position;
    public Annotation? Existing { get; } = existing;
}

public sealed class AnnotationSurface : FrameworkElement
{
    private readonly CaptureDocument _document;
    private List<Point>? _points;
    private AnnotationTool _strokeTool;
    private Color _strokeColor;
    private double _strokeWidth;
    private EditorTool _tool;
    public AnnotationEditor Editor { get; }
    public event EventHandler? ToolChanged;
    public event EventHandler<AnnotationTextRequestedEventArgs>? TextRequested;
    public EditorTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            CancelGesture();
            _tool = value;
            Cursor = value == EditorTool.Select ? Cursors.Arrow : value == EditorTool.Text ? Cursors.IBeam : Cursors.Pen;
            ToolChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }
    public Color InkColor { get; set; } = Color.FromRgb(255, 107, 107);
    public double InkWidth { get; set; } = 4;
    public Rect? InteractionBounds { get; set; }
    public bool HasPendingGesture => _points is not null || Editor.IsDragging;
    private double ImageScale => ActualWidth > 0 ? ActualWidth / _document.Width : 1;

    public AnnotationSurface(CaptureDocument document)
    {
        _document = document;
        Editor = new AnnotationEditor(document);
        Cursor = Cursors.Arrow;
        Focusable = true;
        ClipToBounds = true;
        Editor.Changed += (_, _) => InvalidateVisual();
        LostMouseCapture += (_, _) => CancelGesture();
        Unloaded += (_, _) => CancelGesture();
    }

    public void CancelGesture()
    {
        _points = null;
        Editor.Cancel();
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    public void EditSelectedText()
    {
        CancelGesture();
        if (Editor.Selected is { Tool: AnnotationTool.Text } text)
            TextRequested?.Invoke(this, new AnnotationTextRequestedEventArgs(AnnotationGeometry.Bounds(text).TopLeft, text));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        dc.PushTransform(new ScaleTransform(ActualWidth / _document.Width, ActualHeight / _document.Height));
        dc.DrawImage(_document.Original, new Rect(0, 0, _document.Width, _document.Height));
        foreach (var annotation in _document.Annotations)
            AnnotationRenderer.Draw(dc, Editor.Preview?.Id == annotation.Id ? Editor.Preview : annotation);
        if (_points?.Count >= 2) AnnotationRenderer.Draw(dc, new Annotation(_strokeTool, _strokeColor, _strokeWidth, _points));
        if (Tool == EditorTool.Select && (Editor.Preview ?? Editor.Selected) is { } selected) DrawSelection(dc, selected);
        dc.Pop();
    }

    private void DrawSelection(DrawingContext dc, Annotation annotation)
    {
        double unit = 1 / ImageScale;
        var accent = new SolidColorBrush(Color.FromRgb(120, 231, 195));
        Rect bounds = AnnotationGeometry.Bounds(annotation);
        var outline = new Pen(accent, unit) { DashStyle = DashStyles.Dash };
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(180, 10, 20, 30)), 3 * unit), bounds);
        dc.DrawRectangle(null, outline, bounds);
        foreach (var handle in AnnotationGeometry.Handles(annotation))
        {
            var rect = new Rect(handle.Point.X - 4 * unit, handle.Point.Y - 4 * unit, 8 * unit, 8 * unit);
            dc.DrawRectangle(Brushes.White, new Pen(accent, 1.5 * unit), rect);
        }
    }

    private Point ImagePoint(MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        Rect bounds = InteractionBounds ?? new Rect(0, 0, _document.Width, _document.Height);
        return new Point(Math.Clamp(point.X / ActualWidth * _document.Width, bounds.Left, bounds.Right),
            Math.Clamp(point.Y / ActualHeight * _document.Height, bounds.Top, bounds.Bottom));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        Point point = ImagePoint(e);
        if (Tool == EditorTool.Text)
        {
            TextRequested?.Invoke(this, new AnnotationTextRequestedEventArgs(point));
        }
        else if (Tool == EditorTool.Select)
        {
            if (e.ClickCount == 2 && Editor.HitTest(point, 6 / ImageScale) is { Tool: AnnotationTool.Text } text)
            {
                Editor.Select(text.Id);
                EditSelectedText();
            }
            else if (Editor.Begin(point, 6 / ImageScale)) CaptureMouse();
        }
        else
        {
            Editor.Select(null);
            _strokeTool = Tool switch { EditorTool.Arrow => AnnotationTool.Arrow, EditorTool.Rectangle => AnnotationTool.Rectangle,
                EditorTool.Highlight => AnnotationTool.Highlight, _ => AnnotationTool.Pen };
            _strokeColor = InkColor; _strokeWidth = InkWidth;
            _points = [point];
            CaptureMouse();
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point point = ImagePoint(e);
        if (Editor.IsDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            Editor.Drag(point);
            return;
        }
        if (_points is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            if (_strokeTool == AnnotationTool.Pen)
            {
                if ((_points[^1] - point).Length >= 0.5) _points.Add(point);
            }
            else if (_points.Count == 1) _points.Add(point);
            else _points[1] = point;
            InvalidateVisual();
        }
        else if (Tool == EditorTool.Select)
        {
            var handle = Editor.Selected is { } selected ? AnnotationGeometry.HitHandle(selected, point, 6 / ImageScale) : null;
            Cursor = handle switch
            {
                ResizeHandle.TopLeft or ResizeHandle.BottomRight => Cursors.SizeNWSE,
                ResizeHandle.TopRight or ResizeHandle.BottomLeft => Cursors.SizeNESW,
                ResizeHandle.Start or ResizeHandle.End => Cursors.Cross,
                _ => Editor.HitTest(point, 6 / ImageScale) is not null ? Cursors.SizeAll : Cursors.Arrow
            };
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (Editor.IsDragging)
        {
            Editor.Drag(ImagePoint(e));
            Editor.Commit();
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }
        if (_points is null)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            return;
        }
        var points = _points.ToList();
        var end = ImagePoint(e);
        if (_strokeTool == AnnotationTool.Pen) points.Add(end);
        else if (points.Count == 1) points.Add(end);
        else points[1] = end;
        if (_strokeTool == AnnotationTool.Pen && (points[0] - points[^1]).Length < 0.01 && points.Count == 2)
            points[1] = new Point(end.X + 0.01, end.Y);
        _points = null;
        ReleaseMouseCapture();
        var annotation = new Annotation(_strokeTool, _strokeColor, _strokeWidth, points);
        _document.Add(annotation);
        Tool = EditorTool.Select;
        Editor.Select(annotation.Id);
        InvalidateVisual();
        e.Handled = true;
    }
}
