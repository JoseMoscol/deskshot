using System.ComponentModel;
using DeskShot.Domain;
using DeskShot.Infrastructure;

namespace DeskShot.Presentation;

public partial class EditorWindow : Window
{
    private readonly App _app;
    private readonly CaptureDocument _document;
    private readonly AnnotationSurface _surface;
    private bool _fit = true;
    private long _savedRevision = -1;
    private long _copiedRevision = -1;
    private bool _outputBusy;
    private bool _syncControls;
    private static readonly Color[] Palette = [Color.FromRgb(255, 107, 107), Color.FromRgb(120, 231, 195),
        Color.FromRgb(255, 220, 70), Color.FromRgb(87, 166, 255), Colors.White, Colors.Black];

    public EditorWindow(App app, CaptureDocument document, bool saved = false, bool copied = false)
    {
        _app = app;
        _document = document;
        _surface = new AnnotationSurface(document);
        if (saved) _savedRevision = document.Revision;
        if (copied) _copiedRevision = document.Revision;
        InitializeComponent();
        SurfaceHost.Child = _surface;
        _surface.TextRequested += Surface_TextRequested;
        _surface.ToolChanged += (_, _) => ToolBox.SelectedIndex = (int)_surface.Tool;
        _surface.Editor.SelectionChanged += Selection_Changed;
        DimensionsText.Text = $"{document.Width:N0} × {document.Height:N0} píxeles · resolución original · {document.CreatedAt:HH:mm}";
        _document.Changed += Document_Changed;
        UpdateHistory();
        Loaded += (_, _) => FitImage();
        Closing += OnClosing;
        Closed += (_, _) => { _document.Changed -= Document_Changed; _surface.Editor.Dispose(); };
    }

    private void Document_Changed(object? sender, EventArgs e)
    {
        UpdateHistory();
        EditorStatus.Text = "Cambios pendientes de copiar o guardar.";
    }

    public void SetCaptureNotice(string message) => EditorStatus.Text = message;

    private void UpdateHistory()
    {
        UndoButton.IsEnabled = _document.CanUndo;
        RedoButton.IsEnabled = _document.CanRedo;
        Title = "DeskShot · Editor" + (_document.Revision != _savedRevision && _document.Revision != _copiedRevision ? " *" : "");
    }

    private void Tool_Changed(object sender, SelectionChangedEventArgs e)
    {
        _surface.Tool = (EditorTool)Math.Max(0, ToolBox.SelectedIndex);
        if (_surface.Tool != EditorTool.Select) _surface.Editor.Select(null);
    }
    private void Color_Changed(object sender, SelectionChangedEventArgs e)
    {
        _surface.InkColor = Palette[Math.Clamp(ColorBox.SelectedIndex, 0, Palette.Length - 1)];
        if (!_syncControls && _surface.Editor.Selected is { } selected)
            _surface.Editor.UpdateSelected(selected with { Color = _surface.InkColor });
    }
    private void Width_Changed(object sender, SelectionChangedEventArgs e)
    {
        _surface.InkWidth = new double[] { 2, 4, 8, 12 }[Math.Clamp(WidthBox.SelectedIndex, 0, 3)];
        if (!_syncControls && _surface.Editor.Selected is { Tool: not AnnotationTool.Text } selected)
            _surface.Editor.UpdateSelected(selected with { Width = _surface.InkWidth });
    }
    private void Selection_Changed(object? sender, EventArgs e)
    {
        if (DeleteButton is null) return;
        var selected = _surface.Editor.Selected;
        _syncControls = true;
        try
        {
            DeleteButton.IsEnabled = selected is not null;
            EditTextButton.IsEnabled = selected?.Tool == AnnotationTool.Text;
            WidthBox.IsEnabled = selected?.Tool != AnnotationTool.Text;
            if (selected is not null)
            {
                ColorBox.SelectedIndex = Array.IndexOf(Palette, selected.Color);
                if (selected.Tool != AnnotationTool.Text)
                    WidthBox.SelectedIndex = Array.IndexOf(new double[] { 2, 4, 8, 12 }, selected.Width);
            }
            SelectionHint.Text = selected?.Tool switch
            {
                AnnotationTool.Arrow => "Flecha seleccionada · arrastra para mover o ajusta cualquiera de sus extremos.",
                AnnotationTool.Text => $"Texto · {selected.FontSize:0.#} px · doble clic para editar, esquinas para cambiar tamaño.",
                null => "Selecciona un objeto para moverlo o cambiar su tamaño. Texto (T): haz clic para escribir.",
                _ => "Objeto seleccionado · arrastra para mover o usa las esquinas para cambiar su tamaño."
            };
        }
        finally { _syncControls = false; }
    }

    private void Surface_TextRequested(object? sender, AnnotationTextRequestedEventArgs e)
    {
        var color = e.Existing?.Color ?? _surface.InkColor;
        var dialog = new AnnotationTextDialog((text, size, bold) => AnnotationTextLayout.Create(text, e.Position, color, size, bold,
            new Size(_document.Width, _document.Height), e.Existing?.Id), e.Existing) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } annotation)
        {
            if (e.Existing is null) _document.Add(annotation);
            else _document.Replace(e.Existing.Id, annotation);
            _surface.Tool = EditorTool.Select;
            _surface.Editor.Select(annotation.Id);
        }
        _surface.Focus();
    }
    private void EditText_Click(object sender, RoutedEventArgs e) => _surface.EditSelectedText();
    private void Delete_Click(object sender, RoutedEventArgs e) { _surface.Editor.DeleteSelected(); _surface.Focus(); }
    private void Undo_Click(object sender, RoutedEventArgs e) { _surface.CancelGesture(); _document.Undo(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { _surface.CancelGesture(); _document.Redo(); }
    private void Fit_Click(object sender, RoutedEventArgs e) { _fit = true; FitImage(); }
    private void ImageScroll_SizeChanged(object sender, SizeChangedEventArgs e) { if (_fit) FitImage(); }

    private void FitImage()
    {
        if (ImageScroll is null || ZoomLabel is null) return;
        ZoomBox.SelectedIndex = 0;
        double width = Math.Max(1, ImageScroll.ActualWidth - 80), height = Math.Max(1, ImageScroll.ActualHeight - 80);
        ApplyZoom(Math.Min(1, Math.Min(width / _document.Width, height / _document.Height)));
    }

    private void Zoom_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (ZoomBox.SelectedIndex == 0) { _fit = true; FitImage(); return; }
        _fit = false;
        ApplyZoom(new double[] { 0.5, 1, 1.5, 2 }[Math.Clamp(ZoomBox.SelectedIndex - 1, 0, 3)]);
    }

    private void ApplyZoom(double zoom)
    {
        _surface.Width = _document.Width * zoom;
        _surface.Height = _document.Height * zoom;
        ZoomLabel.Text = $"Vista {zoom:P0} · salida {_document.Width} × {_document.Height} px";
        ZoomBox.ToolTip = _fit ? "Vista ajustada. Elige un porcentaje para cambiarla." : "El zoom solo modifica la vista previa.";
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await SaveAsync(true);
    private async void Copy_Click(object sender, RoutedEventArgs e) => await CopyAsync();
    private void ExtractText_Click(object sender, RoutedEventArgs e) => ShowOcr();
    private void ShowOcr()
    {
        _surface.CancelGesture();
        var window = new OcrWindow(_app.OcrEngine, _document.Original) { Owner = this };
        window.ShowDialog();
    }

    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_outputBusy) return false;
        _surface.CancelGesture();
        _outputBusy = true;
        try
        {
            var settings = _app.Settings;
            ImageFileFormat format = settings.Format;
            string? destination = null;
            if (saveAs)
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Guardar captura", Filter = "PNG sin pérdida (*.png)|*.png|JPEG (*.jpg)|*.jpg",
                    FilterIndex = format == ImageFileFormat.Png ? 1 : 2,
                    FileName = $"DeskShot_{_document.CreatedAt:yyyy-MM-dd_HH-mm-ss}",
                    AddExtension = true, OverwritePrompt = true,
                    InitialDirectory = Directory.Exists(settings.SaveFolder) ? settings.SaveFolder : ""
                };
                if (dialog.ShowDialog(this) != true) return false;
                format = dialog.FilterIndex == 1 ? ImageFileFormat.Png : ImageFileFormat.Jpeg;
                destination = dialog.FileName;
                // El contenido siempre corresponde con la extensión elegida.
                string ext = Path.GetExtension(destination).ToLowerInvariant();
                if (format == ImageFileFormat.Png && ext != ".png" || format == ImageFileFormat.Jpeg && ext is not (".jpg" or ".jpeg"))
                    throw new ArgumentException("La extensión debe coincidir con el formato seleccionado: .png para PNG o .jpg para JPEG.");
            }
            long revision = _document.Revision;
            var image = _app.Exporter.Render(_document);
            string? path = destination;
            EditorStatus.Text = "Guardando a resolución original…";
            await Task.Run(() =>
            {
                if (path is null) path = _app.Exporter.Save(image, settings.SaveFolder, format, settings.JpegQuality);
                else _app.Exporter.SaveTo(image, path, format, settings.JpegQuality);
            });
            _savedRevision = revision;
            UpdateHistory();
            EditorStatus.Text = (_document.Revision == revision ? "Guardado: " : "Versión guardada; tienes cambios nuevos. ") + path;
            _app.Dashboard.SetStatus("Última captura guardada: " + Path.GetFileName(path));
            return _document.Revision == revision;
        }
        catch (Exception ex) { EditorStatus.Text = "No se pudo guardar: " + ex.Message; return false; }
        finally { _outputBusy = false; }
    }

    private async Task CopyAsync()
    {
        if (_outputBusy) return;
        _surface.CancelGesture();
        _outputBusy = true;
        try
        {
            long revision = _document.Revision;
            await App.CopyImageAsync(_app.Exporter.Render(_document));
            _copiedRevision = revision;
            UpdateHistory();
            EditorStatus.Text = "Imagen copiada. Puedes pegarla con Ctrl+V.";
        }
        catch (Exception ex) { EditorStatus.Text = "No se pudo copiar: " + ex.Message; }
        finally { _outputBusy = false; }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.Escape)
            {
                if (_surface.Editor.IsDragging) _surface.CancelGesture();
                else { _surface.CancelGesture(); _surface.Editor.Select(null); }
                e.Handled = true;
                return;
            }
            // No interceptar las flechas de los selectores de herramienta, color o zoom.
            if (Keyboard.FocusedElement is DependencyObject focused)
            {
                for (DependencyObject? parent = focused; parent is not null; parent = VisualTreeHelper.GetParent(parent))
                    if (parent is ComboBox or ComboBoxItem or System.Windows.Controls.Primitives.TextBoxBase) return;
            }
            if (e.Key == Key.Delete) _surface.Editor.DeleteSelected();
            else if (e.Key is Key.F2 or Key.Enter && _surface.Editor.Selected?.Tool == AnnotationTool.Text) _surface.EditSelectedText();
            else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && _surface.Editor.Selected is not null)
            {
                double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
                var delta = e.Key switch { Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0), Key.Up => new Vector(0, -step), _ => new Vector(0, step) };
                _surface.Editor.Nudge(delta);
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.V or Key.P or Key.A or Key.R or Key.H or Key.T)
            {
                ToolBox.SelectedIndex = (int)(e.Key switch { Key.P => EditorTool.Pen, Key.A => EditorTool.Arrow,
                    Key.R => EditorTool.Rectangle, Key.H => EditorTool.Highlight, Key.T => EditorTool.Text, _ => EditorTool.Select });
                _surface.Focus();
            }
            else return;
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Z: _surface.CancelGesture(); _document.Undo(); break;
            case Key.Y: _surface.CancelGesture(); _document.Redo(); break;
            case Key.T: ShowOcr(); break;
            case Key.C: e.Handled = true; await CopyAsync(); return;
            case Key.S: e.Handled = true; await SaveAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); return;
            case Key.D0: _fit = true; FitImage(); break;
            default: return;
        }
        e.Handled = true;
    }

    private bool _closingAfterSave;
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingAfterSave) return;
        if (_outputBusy) { e.Cancel = true; EditorStatus.Text = "Espera a que termine el guardado o la copia."; return; }
        if (_document.Revision == _savedRevision || _document.Revision == _copiedRevision) return;
        var answer = MessageBox.Show(this, "Esta versión todavía no se ha guardado ni copiado. ¿Quieres guardarla antes de cerrar?",
            "Conservar captura", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.No) return;
        e.Cancel = true;
        if (answer == MessageBoxResult.Yes && await SaveAsync(false))
        {
            _closingAfterSave = true;
            Close();
        }
    }
}
