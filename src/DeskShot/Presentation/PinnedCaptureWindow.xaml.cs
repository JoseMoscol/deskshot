using System.Windows.Controls.Primitives;
using DeskShot.Domain;

namespace DeskShot.Presentation;

public partial class PinnedCaptureWindow : Window
{
    private readonly App _app;
    private bool _ready;
    private bool _copying;
    public BitmapSource Snapshot { get; }
    public PinnedCaptureWindow(App app, BitmapSource image)
    {
        _app = app; Snapshot = image.Clone(); Snapshot.Freeze();
        InitializeComponent(); PinnedImage.Source = Snapshot;
        StatusText.Text = $"{Snapshot.PixelWidth} × {Snapshot.PixelHeight} px · copia independiente · Esc para cerrar";
        Loaded += (_, _) => { _ready = true; UpdateScale(); };
    }
    private void UpdateScale()
    {
        if (!_ready) return;
        double scale = ZoomBox.SelectedIndex == 0
            ? Math.Min(1, Math.Min(Math.Max(1, ImageViewer.ViewportWidth) / Snapshot.PixelWidth, Math.Max(1, ImageViewer.ViewportHeight) / Snapshot.PixelHeight))
            : new[] { .5, 1, 1.5, 2 }[Math.Clamp(ZoomBox.SelectedIndex - 1, 0, 3)];
        PinnedImage.Width = Snapshot.PixelWidth * scale; PinnedImage.Height = Snapshot.PixelHeight * scale;
    }
    private void Zoom_Changed(object sender, SelectionChangedEventArgs e) => UpdateScale();
    private void Viewer_SizeChanged(object sender, SizeChangedEventArgs e) { if (ZoomBox.SelectedIndex == 0) UpdateScale(); }
    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => Opacity = e.NewValue / 100;
    private void Reset_Click(object sender, RoutedEventArgs e) { OpacitySlider.Value = 100; ZoomBox.SelectedIndex = 0; UpdateScale(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? item = e.OriginalSource as DependencyObject; item is not null && item != sender; item = item is Visual ? VisualTreeHelper.GetParent(item) : null)
            if (item is ButtonBase or ComboBox or Slider or Thumb) return;
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_copying) return; _copying = true;
        try { await App.CopyImageAsync(Snapshot); StatusText.Text = "Imagen completa copiada. Zoom y opacidad solo afectan a la vista."; }
        catch (Exception ex) { StatusText.Text = "No se pudo copiar: " + ex.Message; }
        finally { _copying = false; }
    }
    private void Editor_Click(object sender, RoutedEventArgs e)
    {
        var editor = new EditorWindow(_app, new CaptureDocument(Snapshot)); editor.Show(); editor.Activate();
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.C) Copy_Click(this, e);
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.D0) Reset_Click(this, e);
        else return;
        e.Handled = true;
    }
}
