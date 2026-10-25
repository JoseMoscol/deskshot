using System.ComponentModel;
using DeskShot.Application;
using DeskShot.Infrastructure;

namespace DeskShot.Presentation;

public partial class UpdatesWindow : Window
{
    private readonly App _app;
    private readonly IUpdateService _service;
    private CancellationTokenSource? _operation;
    private AvailableUpdate? _available;
    private VerifiedUpdate? _verified;
    private bool _closed, _installing;
    public UpdatesWindow(App app, IUpdateService? service = null, bool checkOnLoad = true)
    {
        _app = app; _service = service ?? app.Updates; InitializeComponent();
        VersionText.Text = "Versión instalada: " + App.CurrentVersion.ToString(3);
        if (UpdateHandoff.InstalledDirectory(Environment.ProcessPath!) is null)
        {
            InstallButton.Content = "Instalar edición normal";
            PortableText.Visibility = Visibility.Visible;
        }
        if (checkOnLoad) Loaded += async (_, _) => await CheckAsync();
    }
    private void Busy(bool busy)
    {
        CheckButton.IsEnabled = DownloadButton.IsEnabled = InstallButton.IsEnabled = !busy;
        CancelButton.Visibility = busy && !_installing ? Visibility.Visible : Visibility.Collapsed;
    }
    public async Task CheckAsync()
    {
        if (_operation is not null || _closed) return;
        using var operation = new CancellationTokenSource(); _operation = operation;
        _available = null; _verified = null; Busy(true);
        DownloadButton.Visibility = InstallButton.Visibility = Visibility.Collapsed;
        DownloadProgress.Visibility = Visibility.Collapsed; DownloadText.Text = ""; StatusText.Text = "Buscando una versión estable en GitHub…";
        try
        {
            var update = await _service.CheckAsync(App.CurrentVersion, operation.Token);
            if (_closed) return;
            _available = update;
            StatusText.Text = update is null ? "Ya tienes la versión estable más reciente disponible para esta instalación."
                : $"Disponible: DeskShot {update.Version.ToString(3)} · {update.Installer.Size / 1048576d:N1} MB";
            NotesBox.Text = update?.Notes ?? "Puedes volver a buscar cuando se publique una nueva versión.";
            DownloadButton.Visibility = update is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = operation.IsCancellationRequested ? "Búsqueda cancelada." : "La consulta tardó demasiado. Inténtalo de nuevo."; }
        catch (Exception ex) { if (!_closed) StatusText.Text = "No se pudo buscar la actualización: " + ex.Message; }
        finally { _operation = null; if (!_closed) Busy(false); }
    }
    public async Task DownloadAsync()
    {
        if (_operation is not null || _available is null || _closed) return;
        using var operation = new CancellationTokenSource(); _operation = operation; Busy(true);
        _verified = null; InstallButton.Visibility = Visibility.Collapsed;
        DownloadProgress.Visibility = Visibility.Visible; DownloadProgress.Value = 0; StatusText.Text = "Descargando y verificando el instalador…";
        try
        {
            var progress = new Progress<UpdateDownloadProgress>(p =>
            {
                if (_closed || _operation != operation) return;
                DownloadProgress.Value = p.Percent; DownloadText.Text = $"{p.Percent:N0} % · {p.Received / 1048576d:N1} / {p.Total / 1048576d:N1} MB";
            });
            var verified = await _service.DownloadAsync(_available, progress, operation.Token);
            if (_closed) return;
            _verified = verified; DownloadProgress.Value = 100;
            DownloadText.Text = "SHA-256 verificado.";
            StatusText.Text = "Actualización lista. Se instalará cuando pulses el botón de instalación.";
            DownloadButton.Visibility = Visibility.Collapsed; InstallButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = operation.IsCancellationRequested ? "Descarga cancelada. Puedes volver a intentarlo." : "La descarga tardó demasiado. Inténtalo de nuevo."; }
        catch (Exception ex) { if (!_closed) StatusText.Text = "No se pudo completar la descarga: " + ex.Message; }
        finally { _operation = null; if (!_closed) Busy(false); }
    }
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null || _verified is null) return;
        _installing = true; Busy(true); StatusText.Text = "Preparando la instalación…";
        try
        {
            if (!await _app.InstallUpdateAsync(_verified, _service)) StatusText.Text = "Instalación detenida. Resuelve los cambios pendientes y vuelve a intentarlo cuando quieras.";
        }
        catch (Exception ex) { if (!_closed) StatusText.Text = "No se pudo iniciar la instalación: " + ex.Message; }
        finally { _installing = false; if (!_closed) Busy(false); }
    }
    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckAsync();
    private async void Download_Click(object sender, RoutedEventArgs e) => await DownloadAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_installing && !_app.IsExiting) { e.Cancel = true; return; }
        _closed = true; _operation?.Cancel();
    }
}
