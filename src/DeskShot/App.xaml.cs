using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Interop;
using DeskShot.Application;
using DeskShot.Domain;
using DeskShot.Infrastructure;
using DeskShot.Presentation;
using Forms = System.Windows.Forms;

namespace DeskShot;

public partial class App : System.Windows.Application
{
    private readonly bool _verificationMode;
    public App() { }
    internal App(bool verificationMode) => _verificationMode = verificationMode;
    private readonly JsonSettingsStore _settingsStore = new();
    private readonly ICaptureService _captureService = new GdiCaptureService();
    private CaptureHotkeys? _hotkeys;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private Mutex? _mutex;
    private Mutex? _installerGuard;
    private bool _ownsMutex;
    private bool _capturing;
    private bool _preparingUpdate;
    private UpdatesWindow? _updatesWindow;
    private PdfExportWindow? _pdfWindow;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _trayHintShown;
    public bool IsExiting { get; private set; }
    public AppSettings Settings { get; private set; } = new();
    public IImageExporter Exporter { get; } = new ImageExporter();
    public IOcrEngine OcrEngine { get; } = new TesseractOcrEngine();
    public IUpdateService Updates { get; } = new GitHubUpdateService();
    public static Version CurrentVersion => GitHubUpdateService.Normalize(typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0));
    public MainWindow Dashboard { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (_verificationMode) return;
        string identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        _mutex = new Mutex(true, "Local\\DeskShot-" + identity, out _ownsMutex);
        if (!_ownsMutex)
        {
            MessageBox.Show("DeskShot ya está abierto. Busca su icono en la bandeja del sistema.", "DeskShot");
            Shutdown();
            return;
        }
        // El instalador comprueba su existencia para no actualizar ni quitar
        // una instancia que pueda tener capturas pendientes en el editor.
        _installerGuard = new Mutex(false, "Local\\DeskShotInstallerGuard");
        Settings = _settingsStore.Load();
        Dashboard = new MainWindow(this);
        MainWindow = Dashboard;
        Dashboard.SourceInitialized += (_, _) =>
        {
            _hotkeys = new CaptureHotkeys(new WindowInteropHelper(Dashboard).Handle);
            _hotkeys.CapturePressed += async (_, _) => { if (!Dashboard.IsEditingShortcut) await CaptureAsync(true); };
            _hotkeys.OcrPressed += async (_, _) => { if (!Dashboard.IsEditingShortcut) await CaptureAsync(true, quickOcr: true); };
            try { _hotkeys.Apply(Settings.Shortcut, Settings.OcrShortcut); }
            catch (Exception ex)
            {
                try { _hotkeys.Apply(Settings.Shortcut, null); }
                catch { try { _hotkeys.Apply(null, Settings.OcrShortcut); } catch { } }
                Dashboard.SetStatus("Algún atajo está ocupado; los botones siguen disponibles. " + ex.Message);
            }
        };
        Dashboard.Show();
        CreateTray();
        if (_settingsStore.LoadWarning is not null) Dashboard.SetStatus(_settingsStore.LoadWarning);
        if (Settings.CheckUpdatesOnStartup) _ = CheckUpdatesOnStartupAsync();
    }

    private void CreateTray()
    {
        _trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        _tray = new Forms.NotifyIcon { Icon = _trayIcon, Text = "DeskShot · capturas a resolución nativa", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Capturar región", null, (_, _) => Dispatcher.BeginInvoke(async () => await CaptureAsync(true)));
        menu.Items.Add("Capturar escritorio", null, (_, _) => Dispatcher.BeginInvoke(async () => await CaptureAsync(false)));
        menu.Items.Add("Capturar con desplazamiento", null, (_, _) => Dispatcher.BeginInvoke(async () => await CaptureAsync(true, scrolling: true)));
        menu.Items.Add("OCR rápido · seleccionar y copiar texto", null, (_, _) => Dispatcher.BeginInvoke(async () => await CaptureAsync(true, quickOcr: true)));
        menu.Items.Add("Crear PDF con imágenes", null, (_, _) => Dispatcher.BeginInvoke(ShowPdf));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Abrir DeskShot", null, (_, _) => Dispatcher.BeginInvoke(() => Dashboard.ShowHome()));
        menu.Items.Add("Buscar actualizaciones", null, (_, _) => Dispatcher.BeginInvoke(ShowUpdates));
        menu.Items.Add("Salir", null, (_, _) => Dispatcher.BeginInvoke(ExitApplication));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(() => Dashboard.ShowHome());
    }

    public void NotifyTrayHidden()
    {
        if (_trayHintShown || _tray is null) return;
        _trayHintShown = true;
        _tray.ShowBalloonTip(2500, "DeskShot sigue listo", "Usa tu atajo para capturar o abre DeskShot desde este icono.", Forms.ToolTipIcon.Info);
    }

    public void ApplySettings(AppSettings settings)
    {
        settings.Validate();
        if (_hotkeys is not null) _hotkeys.Apply(settings.Shortcut, settings.OcrShortcut, () => _settingsStore.Save(settings));
        else _settingsStore.Save(settings);
        Settings = settings;
    }

    public async Task CaptureAsync(bool region, bool scrolling = false, bool quickOcr = false)
    {
        if (_capturing || _preparingUpdate || IsExiting || _pdfWindow is not null) return;
        _capturing = true;
        Dashboard.SetBusy(true);
        var visible = Windows.Cast<Window>().Where(w => w.IsVisible).Select(w => (Window: w, State: w.WindowState)).ToList();
        bool restored = false;
        void Restore()
        {
            if (restored) return;
            restored = true;
            foreach (var entry in visible)
            {
                if (!Windows.Cast<Window>().Contains(entry.Window)) continue;
                entry.Window.Show();
                entry.Window.WindowState = entry.State;
            }
        }
        try
        {
            foreach (var entry in visible) entry.Window.Hide();
            await Task.Delay(220 + Settings.CaptureDelaySeconds * 1000);
            NativeMethods.DwmFlush();
            var frame = await Task.Run(_captureService.CaptureDesktop);
            var floating = new FloatingCaptureWindow(this, frame, selectWholeDesktop: !region, scrolling: scrolling, quickOcr: quickOcr,
                targets: region ? new WindowsCaptureTargetDetector(frame.Bounds) : null);
            if (floating.ShowDialog() != true)
            {
                Dashboard.SetStatus("Captura cancelada.");
                return;
            }
            if (floating.ResultAction == FloatingCaptureAction.Completed)
            {
                Dashboard.SetStatus(floating.CompletionMessage);
                return;
            }
            if (floating.ResultAction == FloatingCaptureAction.QuickOcr)
            {
                var textImage = ImageExporter.RenderForOcr(floating.Document);
                Restore();
                new OcrWindow(OcrEngine, textImage, autoCopy: true) { Owner = Dashboard }.ShowDialog();
                Dashboard.SetStatus("OCR rápido terminado. La imagen no se guardó ni sustituyó el texto del portapapeles.");
                return;
            }
            string? scrollMessage = null;
            CaptureDocument document;
            if (floating.ResultAction == FloatingCaptureAction.Scroll)
            {
                var selection = floating.Document.Region;
                var bounds = new PixelBounds(frame.Bounds.X + selection.X, frame.Bounds.Y + selection.Y, selection.Width, selection.Height);
                var scrollWindow = new ScrollingCaptureWindow(bounds);
                if (scrollWindow.ShowDialog() != true || scrollWindow.Result is not { } result)
                { Dashboard.SetStatus("Captura con desplazamiento cancelada."); return; }
                document = new CaptureDocument(result.Image);
                scrollMessage = result.Message;
            }
            else document = floating.Document.CreateRegionDocument();
            var image = Exporter.Render(document);
            bool saved = false, copied = false;
            var notices = new List<string>();
            if (Settings.AutoSave)
            {
                try
                {
                    var settings = Settings;
                    await Task.Run(() => Exporter.Save(image, settings.SaveFolder, settings.Format, settings.JpegQuality));
                    saved = true;
                }
                catch (Exception ex) { notices.Add("Guardado automático falló: " + ex.Message); }
            }
            if (Settings.CopyAfterCapture)
            {
                try { await CopyImageAsync(image); copied = true; }
                catch (Exception ex) { notices.Add("No se pudo copiar: " + ex.Message); }
            }
            Restore();
            var editor = new EditorWindow(this, document, saved, copied);
            editor.Show();
            editor.Activate();
            Dashboard.SetStatus(notices.Count > 0 ? string.Join(" ", notices) : $"Captura de {document.Width} × {document.Height} px lista en el editor. " + scrollMessage);
            if (scrollMessage is not null) editor.SetCaptureNotice(scrollMessage);
            if (notices.Count > 0) MessageBox.Show(editor, string.Join("\n\n", notices) + "\n\nTu captura sigue disponible en el editor.", "Captura disponible", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Restore();
            Dashboard.ShowHome();
            Dashboard.SetStatus("No se pudo capturar: " + ex.Message);
        }
        finally { Restore(); _capturing = false; Dashboard.SetBusy(false); }
    }

    public static async Task CopyImageAsync(BitmapSource image)
    {
        // El portapapeles requiere STA y puede estar ocupado por otra aplicación.
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetImage(image); return; }
            catch (ExternalException) when (attempt < 4) { await Task.Delay(80); }
        }
    }

    public PinnedCaptureWindow PinImage(BitmapSource image)
    {
        var window = new PinnedCaptureWindow(this, image) { ShowInTaskbar = _verificationMode }; window.Show(); window.Activate(); return window;
    }

    public void ShowPdf()
    {
        if (_capturing || _preparingUpdate || IsExiting) return;
        Dashboard.ShowHome();
        OpenPdf(null, Dashboard);
    }
    public PdfExportWindow? OpenPdf(PdfCapture? initial, Window owner)
    {
        if (_capturing || _preparingUpdate || IsExiting) return null;
        if (_pdfWindow is not null) { _pdfWindow.Activate(); return null; }
        var window = new PdfExportWindow(Settings.SaveFolder, initial) { Owner = owner };
        _pdfWindow = window; window.Closed += (_, _) => _pdfWindow = null;
        window.ShowDialog(); return window;
    }

    public static async Task CopyTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("No hay texto para copiar.");
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetText(text, TextDataFormat.UnicodeText); return; }
            catch (ExternalException) when (attempt < 4) { await Task.Delay(80); }
        }
    }

    public void ExitApplication()
    {
        if (IsExiting || _capturing || _preparingUpdate) return;
        if (_pdfWindow is not null) { _pdfWindow.Close(); if (_pdfWindow is not null) return; }
        // Cada editor resuelve sus cambios pendientes antes de terminar la aplicación.
        foreach (var editor in Windows.OfType<EditorWindow>().ToArray())
        {
            editor.Close();
            if (Windows.Cast<Window>().Contains(editor)) return;
        }
        IsExiting = true;
        Shutdown();
    }

    public void ShowUpdates()
    {
        if (_capturing || _preparingUpdate || IsExiting || _pdfWindow is not null) return;
        Dashboard.ShowHome();
        if (_updatesWindow is not null) { _updatesWindow.Show(); _updatesWindow.Activate(); return; }
        var window = new UpdatesWindow(this) { Owner = Dashboard };
        _updatesWindow = window; window.Closed += (_, _) => _updatesWindow = null;
        window.Show(); window.Activate();
    }
    private async Task CheckUpdatesOnStartupAsync()
    {
        try
        {
            var update = await Updates.CheckAsync(CurrentVersion, _lifetime.Token);
            if (update is null || IsExiting) return;
            Dashboard.SetStatus($"DeskShot {update.Version.ToString(3)} está disponible. Abre Actualizaciones para descargarla.");
            _tray?.ShowBalloonTip(4000, "Nueva versión de DeskShot", $"Versión {update.Version.ToString(3)} disponible. Abre Actualizaciones desde el menú de la bandeja.", Forms.ToolTipIcon.Info);
        }
        catch (OperationCanceledException) { }
        catch { /* Una consulta opcional sin conexión no interrumpe las capturas. */ }
    }
    public async Task<bool> InstallUpdateAsync(VerifiedUpdate update, IUpdateService service)
    {
        if (_capturing || _preparingUpdate || IsExiting) return false;
        _preparingUpdate = true; Dashboard?.SetBusy(true);
        try
        {
            await service.ValidateAsync(update, _lifetime.Token);
            if (_pdfWindow is not null) { _pdfWindow.Close(); if (_pdfWindow is not null) return false; }
            if (Windows.OfType<PinnedCaptureWindow>().Any() && MessageBox.Show(_updatesWindow,
                "Las capturas ancladas se cerrarán al actualizar. Copia o guarda las que quieras conservar antes de continuar. ¿Instalar ahora?", "Actualizar DeskShot", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
            foreach (var editor in Windows.OfType<EditorWindow>().ToArray())
            {
                editor.Close(); if (Windows.Cast<Window>().Contains(editor)) return false;
            }
            string appPath = Environment.ProcessPath ?? throw new IOException("No se encuentra la ubicación de DeskShot.");
            if (!string.Equals(Path.GetFileName(appPath), "DeskShot.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Instala la actualización desde DeskShot.exe.");
            using var handoff = await UpdateHandoff.PrepareAsync(update, appPath, _lifetime.Token);
            handoff.Commit(); IsExiting = true; Shutdown(); return true;
        }
        finally { _preparingUpdate = false; if (!IsExiting) Dashboard?.SetBusy(false); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifetime.Cancel();
        _hotkeys?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _trayIcon?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _installerGuard?.Dispose();
        (Updates as IDisposable)?.Dispose(); _lifetime.Dispose();
        base.OnExit(e);
    }
}
