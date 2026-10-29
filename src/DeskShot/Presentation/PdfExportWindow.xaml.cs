using System.Collections.ObjectModel;
using System.ComponentModel;
using DeskShot.Application;
using DeskShot.Infrastructure;

namespace DeskShot.Presentation;

public partial class PdfExportWindow : Window
{
    public ObservableCollection<PdfCapture> Captures { get; } = [];
    private readonly string _folder;
    private readonly BitmapSource? _original;
    private CancellationTokenSource? _export;
    private bool _loading;
    public bool OriginalExported { get; private set; }
    public string? SavedPath { get; private set; }
    public PdfExportWindow(string folder, PdfCapture? initial = null)
    {
        _folder = folder; _original = initial?.Image; InitializeComponent();
        CaptureList.ItemsSource = Captures;
        Captures.CollectionChanged += (_, _) => { if (!_loading) RefreshPlan(); };
        if (initial is not null) Captures.Add(initial);
        CaptureList.SelectedIndex = 0; RefreshPlan();
    }
    private PdfOptions Options => new(PaperBox.SelectedIndex == 1 ? PdfPaper.Letter : PdfPaper.A4, OrientationBox.SelectedIndex == 1);
    private void RefreshPlan()
    {
        if (StatusText is null || OrientationBox is null || _export is not null) return;
        try { StatusText.Text = $"{Captures.Count} capturas · {PdfLayout.Plan(Captures.ToArray(), Options).Count} páginas · píxeles originales"; ExportButton.IsEnabled = !_loading; }
        catch (ArgumentException ex) { StatusText.Text = ex.Message; ExportButton.IsEnabled = false; }
        int i = CaptureList.SelectedIndex;
        UpButton.IsEnabled = i > 0; DownButton.IsEnabled = i >= 0 && i < Captures.Count - 1; RemoveButton.IsEnabled = i >= 0;
    }
    public void MoveCapture(int from, int to)
    { if (_export is not null || _loading || from < 0 || to < 0 || from >= Captures.Count || to >= Captures.Count) return; Captures.Move(from, to); CaptureList.SelectedIndex = to; RefreshPlan(); }
    public void RemoveSelected()
    { if (_export is not null || _loading || CaptureList.SelectedIndex < 0) return; Captures.RemoveAt(CaptureList.SelectedIndex); RefreshPlan(); }
    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_export is not null || _loading) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Añadir capturas al PDF", Filter = "Imágenes (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        _loading = true; CaptureList.IsEnabled = ListControls.IsEnabled = OptionsPanel.IsEnabled = ExportButton.IsEnabled = CloseButton.IsEnabled = false;
        string? error = null;
        try
        {
            // Cargar una a una con límites antes de decodificar; no retener 50 imágenes enormes.
            foreach (string path in dialog.FileNames)
            {
                if (Captures.Count >= PdfLayout.MaxCaptures) throw new ArgumentException("Se admiten hasta 50 capturas.");
                long remaining = PdfLayout.MaxTotalPixels - Captures.Sum(c => (long)c.Image.PixelWidth * c.Image.PixelHeight);
                var image = await Task.Run(() => LoadImage(path, remaining));
                Captures.Add(new(Path.GetFileName(path), image));
            }
            CaptureList.SelectedIndex = Captures.Count - 1; RefreshPlan();
        }
        catch (Exception ex) { error = "No se pudo añadir una imagen: " + ex.Message; }
        finally { _loading = false; CaptureList.IsEnabled = ListControls.IsEnabled = OptionsPanel.IsEnabled = CloseButton.IsEnabled = true; RefreshPlan(); if (error is not null) StatusText.Text = error; }
    }
    internal static BitmapSource LoadImage(string path, long remainingPixels)
    {
        using var file = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        var frame = decoder.Frames[0]; long pixels = (long)frame.PixelWidth * frame.PixelHeight;
        if (pixels > ScrollStitcher.DefaultMaxPixels || pixels > remainingPixels) throw new ArgumentException("La imagen excede el límite de 32 megapíxeles por captura o 160 megapíxeles por PDF.");
        var copy = new WriteableBitmap(frame); copy.Freeze(); return copy;
    }
    public async Task<bool> ExportAsync(string path)
    {
        if (_export is not null || _loading) return false;
        var captures = Captures.ToArray(); var options = Options;
        using var stop = new CancellationTokenSource(); _export = stop;
        ListControls.IsEnabled = OptionsPanel.IsEnabled = CaptureList.IsEnabled = ExportButton.IsEnabled = false;
        CloseButton.Content = "Cancelar"; ExportProgress.Visibility = Visibility.Visible; ExportProgress.Value = 0;
        StatusText.Text = "Creando PDF sin reducir los píxeles…";
        try
        {
            var progress = new Progress<int>(p => { if (_export == stop) ExportProgress.Value = p; });
            await Task.Run(() => new PdfExporter().Save(captures, options, path, progress, stop.Token));
            OriginalExported = captures.Any(c => ReferenceEquals(c.Image, _original)); SavedPath = path;
            StatusText.Text = "PDF guardado: " + path; ExportProgress.Value = 100; return true;
        }
        catch (OperationCanceledException) { StatusText.Text = "Exportación cancelada. El archivo anterior se conserva."; return false; }
        catch (Exception ex) { StatusText.Text = "No se pudo crear el PDF: " + ex.Message; return false; }
        finally { _export = null; ListControls.IsEnabled = OptionsPanel.IsEnabled = CaptureList.IsEnabled = true; CloseButton.Content = "Cerrar"; ExportButton.IsEnabled = Captures.Count > 0; }
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Guardar PDF", Filter = "Documento PDF (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true, OverwritePrompt = true, FileName = $"DeskShot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.pdf", InitialDirectory = Directory.Exists(_folder) ? _folder : "" };
        if (dialog.ShowDialog(this) == true) await ExportAsync(dialog.FileName);
    }
    private void Options_Changed(object sender, SelectionChangedEventArgs e) => RefreshPlan();
    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => RefreshPlan();
    private void Up_Click(object sender, RoutedEventArgs e) => MoveCapture(CaptureList.SelectedIndex, CaptureList.SelectedIndex - 1);
    private void Down_Click(object sender, RoutedEventArgs e) => MoveCapture(CaptureList.SelectedIndex, CaptureList.SelectedIndex + 1);
    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveSelected();
    private void Close_Click(object sender, RoutedEventArgs e) { if (_export is not null) _export.Cancel(); else Close(); }
    private void Window_Closing(object? sender, CancelEventArgs e) { if (_export is not null || _loading) { e.Cancel = true; _export?.Cancel(); } }
}
