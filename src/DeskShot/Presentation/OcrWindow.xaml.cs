using DeskShot.Application;

namespace DeskShot.Presentation;

public partial class OcrWindow : Window
{
    private readonly IOcrEngine _engine;
    private readonly BitmapSource _image;
    private readonly Func<string, Task> _copyText;
    private CancellationTokenSource? _recognition;
    private bool _closed;
    private bool _copying;
    private bool _started;
    private readonly bool _autoCopy;

    public OcrWindow(IOcrEngine engine, BitmapSource original, Func<string, Task>? copyText = null, bool autoCopy = false)
    {
        _engine = engine;
        _image = original;
        _copyText = copyText ?? App.CopyTextAsync;
        _autoCopy = autoCopy;
        InitializeComponent();
        if (autoCopy)
        {
            Title = "DeskShot · OCR rápido · copia automática de texto";
            HeadingText.Text = "OCR rápido";
            DescriptionText.Text = "El texto reconocido se copia automáticamente al portapapeles. La imagen no se guarda. Puedes corregir el resultado y volver a copiarlo.";
        }
        Loaded += async (_, _) => { if (!_started) { _started = true; await ExtractAsync(); } };
        Closed += (_, _) => { _closed = true; _recognition?.Cancel(); };
    }

    private async void Extract_Click(object sender, RoutedEventArgs e) => await ExtractAsync();

    internal async Task ExtractAsync()
    {
        if (_recognition is not null || _closed) return;
        using var cancellation = new CancellationTokenSource();
        _recognition = cancellation;
        SetBusy(true);
        OcrStatus.Text = "Reconociendo texto en tu equipo…";
        try
        {
            string language = LanguageBox.SelectedIndex switch { 1 => "spa", 2 => "eng", _ => "spa+eng" };
            var result = await _engine.RecognizeAsync(_image, language, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested) return;
            ResultBox.Text = result.Text;
            OcrStatus.Text = string.IsNullOrWhiteSpace(result.Text)
                ? "No se detectó texto. Prueba una captura más ajustada y con texto nítido."
                : $"{result.Regions.Count} palabras detectadas · {result.Text.Length:N0} caracteres. Revisa el texto antes de copiarlo.";
            if (_autoCopy && !string.IsNullOrWhiteSpace(result.Text))
            {
                try
                {
                    await _copyText(result.Text);
                    if (!_closed) OcrStatus.Text = "Texto copiado automáticamente. Puedes pegarlo con Ctrl+V o corregirlo aquí.";
                }
                catch (Exception ex) { if (!_closed) OcrStatus.Text = "Texto reconocido; no se pudo copiar: " + ex.Message + " Usa Copiar texto para volver a intentarlo."; }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_closed) OcrStatus.Text = "Extracción cancelada. Puedes volver a intentarlo.";
        }
        catch (Exception ex)
        {
            if (!_closed) OcrStatus.Text = "No se pudo extraer el texto: " + ex.Message;
        }
        finally
        {
            _recognition = null;
            if (!_closed) SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        ExtractButton.IsEnabled = !busy;
        LanguageBox.IsEnabled = !busy;
        ResultBox.IsReadOnly = busy;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = busy;
        OcrProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateCopyButton();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _recognition?.Cancel();
        CancelButton.IsEnabled = false;
        OcrStatus.Text = "Cancelando la extracción…";
    }

    private void Result_TextChanged(object sender, TextChangedEventArgs e) => UpdateCopyButton();
    private void UpdateCopyButton()
    {
        if (CopyTextButton is not null)
            CopyTextButton.IsEnabled = !_copying && _recognition is null && !string.IsNullOrWhiteSpace(ResultBox.Text);
    }

    private async void CopyText_Click(object sender, RoutedEventArgs e) => await CopyTextAsync();
    private async Task CopyTextAsync()
    {
        if (_recognition is not null || _copying || string.IsNullOrWhiteSpace(ResultBox.Text)) return;
        _copying = true;
        UpdateCopyButton();
        try
        {
            await _copyText(ResultBox.Text);
            if (!_closed) OcrStatus.Text = "Texto copiado. Puedes pegarlo con Ctrl+V.";
        }
        catch (Exception ex) { if (!_closed) OcrStatus.Text = "No se pudo copiar el texto: " + ex.Message; }
        finally { _copying = false; if (!_closed) UpdateCopyButton(); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        { e.Handled = true; await ExtractAsync(); }
        else if (e.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        { e.Handled = true; await CopyTextAsync(); }
    }
}
