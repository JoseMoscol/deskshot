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
    private GlobalHotkey? _hotkey;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private Mutex? _mutex;
    private Mutex? _installerGuard;
    private bool _ownsMutex;
    private bool _capturing;
    private bool _trayHintShown;
    public bool IsExiting { get; private set; }
    public AppSettings Settings { get; private set; } = new();
    public IImageExporter Exporter { get; } = new ImageExporter();
    public IOcrEngine OcrEngine { get; } = new TesseractOcrEngine();
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
            _hotkey = new GlobalHotkey(new WindowInteropHelper(Dashboard).Handle);
            _hotkey.Pressed += async (_, _) => { if (!Dashboard.IsEditingShortcut) await CaptureAsync(true); };
            try { _hotkey.Register(Settings.Shortcut); }
            catch (Exception ex) { Dashboard.SetStatus("Captura disponible con los botones. " + ex.Message); }
        };
        Dashboard.Show();
        CreateTray();
        if (_settingsStore.LoadWarning is not null) Dashboard.SetStatus(_settingsStore.LoadWarning);
    }

    private void CreateTray()
    {
        _trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        _tray = new Forms.NotifyIcon { Icon = _trayIcon, Text = "DeskShot · capturas a resolución nativa", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Capturar región", null, (_, _) => Dispatcher.BeginInvoke(async () => await CaptureAsync(true)));
        menu.Items.Add("Capturar escritorio", null, (_, _) => Dispatcher.BeginInvoke(async () => await CaptureAsync(false)));
        menu.Items.Add("Capturar con desplazamiento", null, (_, _) => Dispatcher.BeginInvoke(async () => await CaptureAsync(true, scrolling: true)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Abrir DeskShot", null, (_, _) => Dispatcher.BeginInvoke(() => Dashboard.ShowHome()));
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
        var previous = _hotkey?.Current;
        _hotkey?.Register(settings.Shortcut);
        try { _settingsStore.Save(settings); }
        catch
        {
            if (previous is not null) _hotkey?.Register(previous);
            else _hotkey?.Unregister();
            throw;
        }
        Settings = settings;
    }

    public async Task CaptureAsync(bool region, bool scrolling = false)
    {
        if (_capturing || IsExiting) return;
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
            var floating = new FloatingCaptureWindow(this, frame, selectWholeDesktop: !region, scrolling: scrolling);
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
        if (IsExiting || _capturing) return;
        // Cada editor resuelve sus cambios pendientes antes de terminar la aplicación.
        foreach (var editor in Windows.OfType<EditorWindow>().ToArray())
        {
            editor.Close();
            if (Windows.Cast<Window>().Contains(editor)) return;
        }
        IsExiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _trayIcon?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _installerGuard?.Dispose();
        base.OnExit(e);
    }
}
