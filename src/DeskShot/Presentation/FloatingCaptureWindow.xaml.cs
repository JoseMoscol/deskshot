using System.ComponentModel;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using DeskShot.Domain;
using DeskShot.Infrastructure;
using DeskShot.Application;
using System.Windows.Threading;

namespace DeskShot.Presentation;

public enum FloatingCaptureAction { Cancel, Completed, OpenEditor, Scroll, QuickOcr }

public partial class FloatingCaptureWindow : Window
{
    private readonly App _app;
    private readonly DesktopFrame _frame;
    private readonly AnnotationSurface _surface;
    private readonly CaptureChrome _chrome;
    private bool _hasSelection;
    private bool _areaMode = true;
    private bool _busy;
    private bool _syncProperties;
    private readonly bool _quickOcr;
    private RegionHandle? _regionHandle;
    private Int32Rect _startRegion;
    private Point _startPoint;
    private Int32Rect? _previewRegion;
    private readonly ICaptureTargetDetector? _targets;
    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly CancellationTokenSource _hoverStop = new();
    private IReadOnlyList<CaptureTarget> _candidates = [];
    private CaptureTarget? _hoverTarget, _clickTarget;
    private Point _hoverPoint;
    private bool _automatic = true, _windowOnly, _querying, _closed;
    private int _candidateIndex;
    public CaptureTarget? HoverTarget => _hoverTarget;
    public CaptureDocument Document { get; }
    public FloatingCaptureAction ResultAction { get; private set; }
    public string CompletionMessage { get; private set; } = "Captura cancelada.";
    public bool HasSelection => _hasSelection;
    private static readonly Color[] Palette = [Color.FromRgb(255, 107, 107), Color.FromRgb(120, 231, 195),
        Color.FromRgb(255, 220, 70), Color.FromRgb(87, 166, 255), Colors.White, Colors.Black];

    public FloatingCaptureWindow(App app, DesktopFrame frame, bool selectWholeDesktop = false, bool scrolling = false, bool quickOcr = false, ICaptureTargetDetector? targets = null)
    {
        _targets = targets;
        _quickOcr = quickOcr;
        _app = app;
        _frame = frame;
        Document = new CaptureDocument(frame.Image);
        _surface = new AnnotationSurface(Document);
        _chrome = new CaptureChrome(Document.Width, Document.Height);
        InitializeComponent();
        ExtraOptions.Bind(_surface);
        if (scrolling) Status("Selecciona aproximadamente el documento. Centra la selección sobre la zona que se desplaza; podrás activar la detección automática al continuar con Desplazar.");
        if (quickOcr) Status("OCR rápido: arrastra sobre el texto y suelta para extraerlo y copiarlo. Esc cancela. No se guarda la imagen.");
        SurfaceHost.Children.Add(_surface);
        ChromeHost.Children.Add(_chrome);
        _surface.ToolChanged += (_, _) => { _areaMode = false; RefreshToolButtons(); };
        _surface.TextRequested += Surface_TextRequested;
        _surface.Editor.SelectionChanged += (_, _) => RefreshProperties();
        Document.Changed += Document_Changed;
        LostMouseCapture += (_, _) => { if (_regionHandle is not null && !Root.IsMouseCaptured) CancelRegionGesture(); };
        _hoverTimer.Tick += async (_, _) => await DetectHoverAsync();
        Loaded += (_, _) =>
        {
            var bounds = frame.Bounds;
            if (!NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, new nint(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040))
                throw new InvalidOperationException("No se pudo mostrar el modo de captura flotante.");
            Activate(); Focus(); RefreshRegion();
            if (_targets is not null && !selectWholeDesktop)
            {
                if (NativeMethods.GetCursorPos(out var cursor)) TrackHover(new(cursor.X - frame.Bounds.X, cursor.Y - frame.Bounds.Y));
                _hoverTimer.Start();
            }
        };
        Closed += (_, _) => { _closed = true; _hoverTimer.Stop(); _hoverStop.Cancel(); if (!_querying) _hoverStop.Dispose(); Document.Changed -= Document_Changed; ExtraOptions.Dispose(); _surface.Editor.Dispose(); };
        if (selectWholeDesktop) SelectRegion(new Int32Rect(0, 0, Document.Width, Document.Height));
        RefreshToolButtons(); RefreshProperties();
    }

    public void SelectRegion(Int32Rect region)
    {
        _hasSelection = true;
        _hoverTarget = null; _candidates = [];
        Document.SetRegion(region);
        RefreshRegion();
    }

    private void Document_Changed(object? sender, EventArgs e) { RefreshRegion(); RefreshProperties(); }
    private Int32Rect CurrentRegion => _previewRegion ?? Document.Region;
    private double ScaleX => Root.ActualWidth > 0 ? Root.ActualWidth / Document.Width : 1;
    private double ScaleY => Root.ActualHeight > 0 ? Root.ActualHeight / Document.Height : 1;
    private Rect ToView(Int32Rect region) => new(region.X * ScaleX, region.Y * ScaleY, region.Width * ScaleX, region.Height * ScaleY);
    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshRegion();

    private void RefreshRegion()
    {
        if (Root is null) return;
        var region = CurrentRegion;
        _chrome.Region = _hasSelection || _regionHandle is not null ? region : null;
        _chrome.Target = !_hasSelection && _regionHandle is null ? _hoverTarget : null;
        _chrome.Hint = _targets is null ? "Arrastra para seleccionar · Esc para cancelar" : !_automatic ? "Selección manual · arrastra para elegir · M automática · Esc cancela"
            : _windowOnly ? "Ventanas · clic para elegir · W paneles · M manual · arrastra para recortar"
            : "Clic para elegir área · Tab cambia el nivel · W ventana · M manual · arrastra para recortar";
        _chrome.InvalidateVisual();
        _surface.InteractionBounds = _hasSelection ? new Rect(region.X, region.Y, region.Width, region.Height) : null;
        bool show = _hasSelection && _regionHandle is null && !_quickOcr;
        ToolsPanel.Visibility = ActionsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _surface.IsHitTestVisible = show && !_busy;
        if (!show || Root.ActualWidth <= 0 || Root.ActualHeight <= 0) return;
        Rect viewport = ToolbarViewport(region);
        ActionsPanel.MaxWidth = Math.Max(1, viewport.Width - 16);
        ToolsPanel.MaxHeight = Math.Max(1, viewport.Height - 16);
        ToolsPanel.Measure(new Size(double.PositiveInfinity, ToolsPanel.MaxHeight));
        ActionsPanel.Measure(new Size(ActionsPanel.MaxWidth, double.PositiveInfinity));
        var placement = FloatingToolbarLayout.Place(ToView(region), viewport, ToolsPanel.DesiredSize, ActionsPanel.DesiredSize);
        Canvas.SetLeft(ToolsPanel, placement.Tools.X); Canvas.SetTop(ToolsPanel, placement.Tools.Y);
        Canvas.SetLeft(ActionsPanel, placement.Actions.X); Canvas.SetTop(ActionsPanel, placement.Actions.Y);
    }

    internal void TrackHover(Point point)
    {
        _hoverPoint = point;
        if (_closed || _targets is null || !_automatic || _hasSelection || _regionHandle is not null) return;
        var window = _targets.WindowAt(point);
        if (_windowOnly || _hoverTarget is null || window?.WindowId != _hoverTarget.WindowId || !CaptureTargetGeometry.Contains(_hoverTarget.Region, point))
        { _hoverTarget = window; _candidates = window is null ? [] : [window]; _candidateIndex = 0; }
        RefreshRegion();
    }
    internal async Task DetectHoverAsync()
    {
        if (_closed || _querying || _targets is null || !_automatic || _windowOnly || _hasSelection || _regionHandle is not null) return;
        _querying = true; var point = _hoverPoint;
        try
        {
            var found = await _targets.QueryAsync(point, _hoverStop.Token);
            if (_closed || !_automatic || _windowOnly || _hasSelection || _regionHandle is not null || (_hoverPoint - point).Length > 3) return;
            var ordered = CaptureTargetGeometry.Order(found, point);
            if (ordered.Count == 0) return;
            bool same = _candidates.Select(c => c.Region).SequenceEqual(ordered.Select(c => c.Region));
            _candidates = ordered; _candidateIndex = same ? Math.Min(_candidateIndex, ordered.Count - 1) : 0;
            _hoverTarget = _candidates[_candidateIndex]; RefreshRegion();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* La selección manual sigue disponible. */ }
        finally { _querying = false; if (_closed) _hoverStop.Dispose(); }
    }
    internal void CycleTarget()
    {
        if (_hasSelection || _candidates.Count == 0) return;
        _candidateIndex = (_candidateIndex + 1) % _candidates.Count;
        _hoverTarget = _candidates[_candidateIndex]; RefreshRegion();
    }
    internal void ToggleAutomatic()
    {
        _automatic = !_automatic; _hoverTarget = null; _candidates = [];
        TrackHover(_hoverPoint); RefreshRegion();
    }
    internal void ToggleWindowOnly()
    {
        _windowOnly = !_windowOnly; _automatic = true; _hoverTarget = null; _candidates = [];
        TrackHover(_hoverPoint); RefreshRegion();
    }

    private Rect ToolbarViewport(Int32Rect region)
    {
        var monitors = _frame.Monitors;
        if (monitors is null || monitors.Count == 0) return new Rect(0, 0, Root.ActualWidth, Root.ActualHeight);
        var selection = new Rect(_frame.Bounds.X + region.X, _frame.Bounds.Y + region.Y, region.Width, region.Height);
        var best = monitors.OrderByDescending(m =>
        {
            var intersection = Rect.Intersect(selection, new Rect(m.X, m.Y, m.Width, m.Height));
            return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
        }).First();
        return new Rect((best.X - _frame.Bounds.X) * ScaleX, (best.Y - _frame.Bounds.Y) * ScaleY,
            best.Width * ScaleX, best.Height * ScaleY);
    }

    private Point Pointer(MouseEventArgs e)
    {
        // La imagen ocupa todo el visual. Convertir sus DIPs a píxeles evita mezclar
        // coordenadas globales virtualizadas con las físicas en escalados de Windows.
        var point = e.GetPosition(Root);
        return new Point(Math.Clamp(Math.Round(point.X / ScaleX), 0, Document.Width), Math.Clamp(Math.Round(point.Y / ScaleY), 0, Document.Height));
    }

    private static bool IsControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or ComboBox or ComboBoxItem or TextBoxBase) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_busy || IsControl(e.OriginalSource as DependencyObject)) return;
        Point point = Pointer(e);
        _clickTarget = !_hasSelection && _automatic && _hoverTarget is { } target && CaptureTargetGeometry.Contains(target.Region, point) ? target : null;
        RegionHandle? handle = _hasSelection ? RegionGeometry.HitHandle(Document.Region, point, 6 / ScaleX) : RegionHandle.New;
        var region = Document.Region;
        bool inside = new Rect(region.X, region.Y, region.Width, region.Height).Contains(point);
        if (handle is null && _areaMode) handle = inside ? RegionHandle.Move : RegionHandle.New;
        if (handle is null)
        {
            if (!inside) e.Handled = true;
            return;
        }
        _surface.CancelGesture(); _surface.Editor.Select(null);
        _regionHandle = handle;
        _startRegion = Document.Region;
        _startPoint = point;
        _previewRegion = handle == RegionHandle.New ? SelectionMath.FromPoints(point, point, Document.Width, Document.Height) : _startRegion;
        Root.CaptureMouse();
        RefreshRegion();
        e.Handled = true;
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (_regionHandle is { } handle)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _previewRegion = RegionGeometry.Adjust(_startRegion, handle, _startPoint, Pointer(e), Document.Width, Document.Height);
                RefreshRegion(); e.Handled = true;
            }
            return;
        }
        if (_busy || IsControl(e.OriginalSource as DependencyObject)) return;
        if (!_hasSelection) { Cursor = Cursors.Cross; TrackHover(Pointer(e)); return; }
        var hit = RegionGeometry.HitHandle(Document.Region, Pointer(e), 6 / ScaleX);
        Cursor = hit switch
        {
            RegionHandle.TopLeft or RegionHandle.BottomRight => Cursors.SizeNWSE,
            RegionHandle.TopRight or RegionHandle.BottomLeft => Cursors.SizeNESW,
            RegionHandle.Top or RegionHandle.Bottom => Cursors.SizeNS,
            RegionHandle.Left or RegionHandle.Right => Cursors.SizeWE,
            _ => _areaMode ? Cursors.SizeAll : null
        };
        if (hit is not null || _areaMode) _surface.Cursor = Cursor;
    }

    private void Root_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_regionHandle is not { } handle) return;
        var region = RegionGeometry.Adjust(_startRegion, handle, _startPoint, Pointer(e), Document.Width, Document.Height);
        if (handle == RegionHandle.New && !_hasSelection && _clickTarget is { } target && CaptureTargetGeometry.IsClick(_startPoint, Pointer(e), 1 / Math.Max(.001, ScaleX))) region = target.Region;
        _clickTarget = null;
        _regionHandle = null; _previewRegion = null;
        Root.ReleaseMouseCapture();
        if (region.Width >= 2 && region.Height >= 2) SelectRegion(region);
        else RefreshRegion();
        if (_quickOcr && region.Width >= 2 && region.Height >= 2)
        { ResultAction = FloatingCaptureAction.QuickOcr; DialogResult = true; }
        e.Handled = true;
    }

    private void CancelRegionGesture()
    {
        _regionHandle = null; _previewRegion = null;
        if (Root.IsMouseCaptured) Root.ReleaseMouseCapture();
        RefreshRegion();
    }

    private void Area_Click(object sender, RoutedEventArgs e) => SetAreaMode();
    private void SetAreaMode()
    {
        _surface.CancelGesture(); _surface.Editor.Select(null);
        _surface.Tool = EditorTool.Select;
        _areaMode = true;
        RefreshToolButtons();
        RefreshProperties();
        Root.Focus();
    }
    private void Tool_Click(object sender, RoutedEventArgs e) => SetTool((EditorTool)int.Parse((string)((Button)sender).Tag));
    private void SetTool(EditorTool tool)
    {
        _areaMode = false;
        _surface.Tool = tool;
        Cursor = null;
        _surface.Cursor = tool == EditorTool.Select ? Cursors.Arrow : tool == EditorTool.Text ? Cursors.IBeam : Cursors.Pen;
        if (tool != EditorTool.Select) _surface.Editor.Select(null);
        RefreshToolButtons(); _surface.Focus();
    }
    private void RefreshToolButtons()
    {
        if (AreaButton is null) return;
        var buttons = new[] { SelectButton, PenButton, ArrowButton, RectangleButton, HighlightButton, TextButton, RedactButton, PixelateButton, BlurButton, StepButton };
        void Style(Button button, bool active)
        {
            button.Background = active ? new SolidColorBrush(Color.FromRgb(120, 231, 195)) : Brushes.Transparent;
            button.Foreground = active ? new SolidColorBrush(Color.FromRgb(10, 41, 33)) : Brushes.White;
        }
        Style(AreaButton, _areaMode);
        for (int i = 0; i < buttons.Length; i++) Style(buttons[i], !_areaMode && (int)_surface.Tool == i);
    }

    private void RefreshProperties()
    {
        if (UndoButton is null) return;
        UndoButton.IsEnabled = Document.CanUndo; RedoButton.IsEnabled = Document.CanRedo;
        DeleteButton.IsEnabled = _surface.Editor.Selected is not null;
        var selected = _surface.Editor.Selected;
        _syncProperties = true;
        try
        {
            if (selected is not null)
            {
                _surface.InkColor = selected.Color;
                if (selected.Tool is AnnotationTool.Pen or AnnotationTool.Arrow or AnnotationTool.Rectangle or AnnotationTool.Highlight)
                    WidthBox.SelectedIndex = Array.IndexOf(new double[] { 2, 4, 8, 12 }, selected.Width);
            }
            WidthBox.IsEnabled = selected is null ? _surface.Tool is EditorTool.Pen or EditorTool.Arrow or EditorTool.Rectangle or EditorTool.Highlight
                : selected.Tool is AnnotationTool.Pen or AnnotationTool.Arrow or AnnotationTool.Rectangle or AnnotationTool.Highlight;
            PalettePanel.IsEnabled = selected is null ? _surface.Tool is not (EditorTool.Pixelate or EditorTool.Blur) : selected.Tool is not (AnnotationTool.Pixelate or AnnotationTool.Blur);
            ColorSwatch.Fill = new SolidColorBrush(_surface.InkColor);
        }
        finally { _syncProperties = false; }
    }
    private void Color_Click(object sender, RoutedEventArgs e) => ColorPopup.IsOpen = !ColorPopup.IsOpen;
    private void Palette_Click(object sender, RoutedEventArgs e)
    {
        _surface.InkColor = Palette[int.Parse((string)((Button)sender).Tag)];
        if (_surface.Editor.Selected is { } selected) _surface.Editor.UpdateSelected(selected with { Color = _surface.InkColor });
        RefreshProperties();
    }
    private void Width_Changed(object sender, SelectionChangedEventArgs e)
    {
        _surface.InkWidth = new double[] { 2, 4, 8, 12 }[Math.Clamp(WidthBox.SelectedIndex, 0, 3)];
        if (!_syncProperties && _surface.Editor.Selected is { Tool: AnnotationTool.Pen or AnnotationTool.Arrow or AnnotationTool.Rectangle or AnnotationTool.Highlight } selected)
            _surface.Editor.UpdateSelected(selected with { Width = _surface.InkWidth });
    }
    private void Undo_Click(object sender, RoutedEventArgs e) { _surface.CancelGesture(); Document.Undo(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { _surface.CancelGesture(); Document.Redo(); }
    private void Delete_Click(object sender, RoutedEventArgs e) => _surface.Editor.DeleteSelected();

    private void Surface_TextRequested(object? sender, AnnotationTextRequestedEventArgs e)
    {
        var region = Document.Region;
        Color color = e.Existing?.Color ?? _surface.InkColor;
        var dialog = new AnnotationTextDialog((text, size, bold) =>
        {
            var annotation = AnnotationTextLayout.Create(text, new Point(e.Position.X - region.X, e.Position.Y - region.Y),
                color, size, bold, new Size(region.Width, region.Height), e.Existing?.Id);
            return annotation with { Points = annotation.Points.Select(p => new Point(p.X + region.X, p.Y + region.Y)).ToArray() };
        }, e.Existing) { Owner = this, Topmost = true };
        if (dialog.ShowDialog() == true && dialog.Result is { } result)
        {
            if (e.Existing is null) Document.Add(result); else Document.Replace(result.Id, result);
            SetTool(EditorTool.Select); _surface.Editor.Select(result.Id);
        }
        _surface.Focus();
    }

    private void Status(string message) { StatusText.Text = message; StatusPanel.Visibility = Visibility.Visible; }
    private void SetBusy(bool value)
    {
        _busy = value;
        ToolsPanel.IsEnabled = ActionsPanel.IsEnabled = !value;
        _surface.IsHitTestVisible = !value;
        ColorPopup.IsOpen = false;
    }
    private async void Copy_Click(object sender, RoutedEventArgs e) => await OutputAsync(false);
    private async void Save_Click(object sender, RoutedEventArgs e) => await OutputAsync(true);
    private async Task OutputAsync(bool save, bool saveAs = false)
    {
        if (_busy || !_hasSelection) return;
        _surface.CancelGesture(); SetBusy(true);
        string? path = null;
        bool written = false, copied = false;
        try
        {
            var settings = _app.Settings;
            var format = settings.Format;
            string? destination = null;
            if (saveAs)
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Guardar captura", Filter = "PNG sin pérdida (*.png)|*.png|JPEG (*.jpg)|*.jpg",
                    FilterIndex = format == ImageFileFormat.Png ? 1 : 2, AddExtension = true, OverwritePrompt = true,
                    FileName = $"DeskShot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}",
                    InitialDirectory = Directory.Exists(settings.SaveFolder) ? settings.SaveFolder : ""
                };
                if (dialog.ShowDialog(this) != true) return;
                destination = dialog.FileName;
                format = dialog.FilterIndex == 1 ? ImageFileFormat.Png : ImageFileFormat.Jpeg;
                string extension = Path.GetExtension(destination).ToLowerInvariant();
                if (format == ImageFileFormat.Png && extension != ".png" || format == ImageFileFormat.Jpeg && extension is not (".jpg" or ".jpeg"))
                    throw new ArgumentException("La extensión debe coincidir con el formato seleccionado.");
            }
            Status(save ? "Guardando a resolución original…" : "Copiando imagen…");
            var image = _app.Exporter.Render(Document);
            if (!save) { await App.CopyImageAsync(image); copied = true; }
            if (save || settings.AutoSave)
            {
                path = destination;
                await Task.Run(() =>
                {
                    if (path is null) path = _app.Exporter.Save(image, settings.SaveFolder, format, settings.JpegQuality);
                    else _app.Exporter.SaveTo(image, path, format, settings.JpegQuality);
                });
                written = true;
            }
            if (save && settings.CopyAfterCapture) await App.CopyImageAsync(image);
            CompletionMessage = path is null ? $"Captura de {image.PixelWidth} × {image.PixelHeight} px copiada."
                : "Captura guardada: " + path;
            ResultAction = FloatingCaptureAction.Completed;
            _busy = false;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Status((written ? "Archivo guardado en " + path + ". " : copied ? "Imagen copiada. " : "") + "No se pudo completar la operación: " + ex.Message);
        }
        finally { SetBusy(false); }
    }

    private void Ocr_Click(object sender, RoutedEventArgs e) => ShowOcr();
    private void ShowOcr()
    {
        if (_busy || !_hasSelection) return;
        _surface.CancelGesture();
        new OcrWindow(_app.OcrEngine, ImageExporter.RenderForOcr(Document)) { Owner = this, Topmost = true }.ShowDialog();
    }
    private void Editor_Click(object sender, RoutedEventArgs e) => OpenEditor();
    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_hasSelection) return;
        _surface.CancelGesture();
        try
        {
            _app.PinImage(_app.Exporter.Render(Document));
            CompletionMessage = "Captura anclada. Puedes copiarla o abrirla en el editor desde su barra.";
            ResultAction = FloatingCaptureAction.Completed; DialogResult = true;
        }
        catch (Exception ex) { Status("No se pudo anclar: " + ex.Message); }
    }
    private void Scroll_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_hasSelection) return;
        if (Document.Region.Width < 64 || Document.Region.Height < 100)
        { Status("Selecciona un área de al menos 64 × 100 píxeles para capturar con desplazamiento."); return; }
        if (Document.Annotations.Count > 0)
        { Status("La captura con desplazamiento usa el documento real. Deshaz las anotaciones y añádelas después, en el editor de la captura completa."); return; }
        _surface.CancelGesture(); ResultAction = FloatingCaptureAction.Scroll; DialogResult = true;
    }
    private void OpenEditor()
    {
        if (_busy || !_hasSelection) return;
        _surface.CancelGesture(); ResultAction = FloatingCaptureAction.OpenEditor; DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_busy) { e.Cancel = true; Status("Espera a que termine la copia o el guardado."); }
    }
    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_hasSelection && _regionHandle is null && _targets is not null && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.Tab) { CycleTarget(); e.Handled = true; return; }
            if (e.Key == Key.W) { ToggleWindowOnly(); e.Handled = true; return; }
            if (e.Key == Key.M) { ToggleAutomatic(); e.Handled = true; return; }
        }
        if (_quickOcr)
        {
            if (e.Key == Key.Escape) Close();
            e.Handled = true; return;
        }
        if (Keyboard.FocusedElement is TextBoxBase && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            && e.Key is Key.C or Key.X or Key.V or Key.A or Key.Z or Key.Y) return;
        if (_busy) { e.Handled = true; return; }
        if (e.Key == Key.Escape)
        {
            if (ColorPopup.IsOpen) ColorPopup.IsOpen = false;
            else if (_regionHandle is not null) CancelRegionGesture();
            else if (_surface.HasPendingGesture) _surface.CancelGesture();
            else Close();
            e.Handled = true; return;
        }
        if (_regionHandle is not null) { e.Handled = true; return; }
        if (!_hasSelection) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            switch (e.Key)
            {
                case Key.C: e.Handled = true; await OutputAsync(false); return;
                case Key.S: e.Handled = true; await OutputAsync(true, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); return;
                case Key.T: ShowOcr(); break;
                case Key.E: OpenEditor(); break;
                case Key.I: Pin_Click(this, e); break;
                case Key.Z: _surface.CancelGesture(); Document.Undo(); break;
                case Key.Y: _surface.CancelGesture(); Document.Redo(); break;
                default: return;
            }
            e.Handled = true; return;
        }
        if (IsControl(Keyboard.FocusedElement as DependencyObject) && Keyboard.FocusedElement is ComboBox or ComboBoxItem or TextBoxBase) return;
        if (e.Key == Key.Return && _surface.Editor.Selected?.Tool != AnnotationTool.Text) { e.Handled = true; await OutputAsync(false); return; }
        if (e.Key is Key.F2 or Key.Return && _surface.Editor.Selected?.Tool == AnnotationTool.Text) _surface.EditSelectedText();
        else if (e.Key == Key.Delete) _surface.Editor.DeleteSelected();
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var delta = e.Key switch { Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0), Key.Up => new Vector(0, -step), _ => new Vector(0, step) };
            if (_areaMode)
                Document.SetRegion(RegionGeometry.Adjust(Document.Region, RegionHandle.Move, new Point(0, 0), new Point(delta.X, delta.Y), Document.Width, Document.Height));
            else _surface.Editor.Nudge(delta);
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.B) SetAreaMode();
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.V or Key.P or Key.A or Key.R or Key.H or Key.T or Key.C or Key.X or Key.D or Key.N)
            SetTool(e.Key switch { Key.P => EditorTool.Pen, Key.A => EditorTool.Arrow, Key.R => EditorTool.Rectangle,
                Key.H => EditorTool.Highlight, Key.T => EditorTool.Text, Key.C => EditorTool.Redact,
                Key.X => EditorTool.Pixelate, Key.D => EditorTool.Blur, Key.N => EditorTool.Step, _ => EditorTool.Select });
        else return;
        e.Handled = true;
    }
}
