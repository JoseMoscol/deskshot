using System.ComponentModel;
using System.Windows.Interop;
using DeskShot.Application;
using DeskShot.Domain;
using DeskShot.Infrastructure;

namespace DeskShot.Presentation;

public partial class ScrollingCaptureWindow : Window
{
    private readonly PixelBounds _bounds;
    private readonly CancellationTokenSource _stop = new();
    private bool _running;
    private NativeMethods.NativeRect _controlsBounds;
    public ScrollingCaptureResult? Result { get; private set; }
    public ScrollingCaptureWindow(PixelBounds bounds)
    {
        _bounds = bounds; InitializeComponent();
        ProgressText.Text = $"Área: {bounds.Width} × {bounds.Height} px · resolución nativa";
        Closed += (_, _) => _stop.Dispose();
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        if (_bounds.Width < 64 || _bounds.Height < 100 || (long)_bounds.Width * _bounds.Height > ScrollStitcher.DefaultMaxPixels)
        { ProgressText.Text = "Elige un área de al menos 64 × 100 px y como máximo 32 megapíxeles."; return; }
        int wait = new[] { 600, 1500, 3000 }[Math.Clamp(WaitBox.SelectedIndex, 0, 2)];
        _running = true; SetupPanel.Visibility = StartButton.Visibility = CancelButton.Visibility = Visibility.Collapsed;
        FinishButton.Visibility = Visibility.Visible; Heading.Text = "Capturando… Esc para terminar";
        ShowActivated = false;
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.GetWindowRect(hwnd, out _controlsBounds);
        int style = NativeMethods.GetWindowLong(hwnd, -20);
        NativeMethods.SetWindowLong(hwnd, -20, style | 0x08000000);
        try
        {
            SetControlsVisible(false); await Task.Delay(300);
            var source = new WindowsScrollingSource(_bounds, wait, SetControlsVisible);
            source.Prepare();
            Result = await new ScrollingCaptureSession().CaptureAsync(source,
                new Progress<ScrollProgress>(p => ProgressText.Text = $"{p.Width} × {p.Height} px · {p.Frames} partes unidas. Esc para terminar."), _stop.Token);
            _running = false; DialogResult = true;
        }
        catch (OperationCanceledException) { _running = false; DialogResult = false; }
        catch (Exception ex)
        {
            _running = false; SetControlsVisible(true); Heading.Text = "No se pudo iniciar"; ProgressText.Text = ex.Message;
            FinishButton.Visibility = Visibility.Collapsed; CancelButton.Visibility = Visibility.Visible;
        }
    }
    private void SetControlsVisible(bool visible)
    {
        // Hide() terminaría el bucle modal de ShowDialog y perdería la sesión.
        int x = visible ? _controlsBounds.Left : NativeMethods.GetSystemMetrics(76) - 10000;
        int y = visible ? _controlsBounds.Top : NativeMethods.GetSystemMetrics(77);
        if (!NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, 0, x, y, 0, 0, 0x0015))
            throw new InvalidOperationException("No se pudo retirar la barra de controles de la captura.");
    }
    private void Finish_Click(object sender, RoutedEventArgs e) => _stop.Cancel();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e)
    { if (_running) { e.Cancel = true; _stop.Cancel(); } }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { if (_running) _stop.Cancel(); else Close(); e.Handled = true; } }
}
