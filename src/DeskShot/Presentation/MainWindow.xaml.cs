using System.ComponentModel;
using System.Diagnostics;
using DeskShot.Domain;

namespace DeskShot.Presentation;

public partial class MainWindow : Window
{
    private readonly App _app;
    private ShortcutSettings _pendingShortcut = ShortcutSettings.Default;
    private ShortcutSettings _pendingOcrShortcut = ShortcutSettings.OcrDefault;

    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        VersionText.Text = $"Versión {App.CurrentVersion.ToString(3)}";
        LoadSettings();
        Closing += OnClosing;
    }

    public void SetStatus(string message) => StatusText.Text = message;
    public void SetBusy(bool busy) { RegionButton.IsEnabled = DesktopButton.IsEnabled = ScrollButton.IsEnabled = QuickOcrButton.IsEnabled = PdfButton.IsEnabled = UpdatesButton.IsEnabled = !busy; }
    public bool IsEditingShortcut => IsVisible && (ShortcutBox.IsKeyboardFocusWithin || OcrShortcutBox.IsKeyboardFocusWithin);

    public void ShowHome()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void Home_Click(object sender, RoutedEventArgs e) => Navigate(false);
    private void Settings_Click(object sender, RoutedEventArgs e) => Navigate(true);
    private void Navigate(bool settings)
    {
        HomePanel.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsPanel.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        HomeNav.Tag = settings ? null : "Active";
        SettingsNav.Tag = settings ? "Active" : null;
        SavePreferencesButton.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        PageScroll.ScrollToTop();
    }

    private async void CaptureRegion_Click(object sender, RoutedEventArgs e) => await _app.CaptureAsync(true);
    private async void CaptureDesktop_Click(object sender, RoutedEventArgs e) => await _app.CaptureAsync(false);
    private async void CaptureScroll_Click(object sender, RoutedEventArgs e) => await _app.CaptureAsync(true, scrolling: true);
    private void Exit_Click(object sender, RoutedEventArgs e) => _app.ExitApplication();
    private void Updates_Click(object sender, RoutedEventArgs e) => _app.ShowUpdates();
    private void Pdf_Click(object sender, RoutedEventArgs e) => _app.ShowPdf();
    private async void QuickOcr_Click(object sender, RoutedEventArgs e) => await _app.CaptureAsync(true, quickOcr: true);

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_app.Settings.SaveFolder);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { _app.Settings.SaveFolder } });
        }
        catch (Exception ex) { SetStatus("No se pudo abrir la carpeta: " + ex.Message); }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta para tus capturas" };
        if (Directory.Exists(FolderBox.Text)) dialog.InitialDirectory = FolderBox.Text;
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private void ShortcutBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) return;
        e.Handled = true;
        var shortcut = new ShortcutSettings(Keyboard.Modifiers, key);
        if (!shortcut.IsValid)
        {
            SetStatus("Combina Ctrl, Alt, Shift o Win con una letra, número o tecla F.");
            return;
        }
        if (ReferenceEquals(sender, OcrShortcutBox)) { _pendingOcrShortcut = shortcut; OcrShortcutBox.Text = shortcut.ToString(); }
        else { _pendingShortcut = shortcut; ShortcutBox.Text = shortcut.ToString(); }
        SetStatus("Pulsa Guardar configuración para aplicar el nuevo atajo.");
    }

    private void FormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QualityBox is not null) QualityBox.IsEnabled = FormatBox.SelectedIndex == 1;
    }

    private void AppearanceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppearanceBox.SelectedValue is not AppAppearance appearance || appearance == _app.Settings.Appearance) return;
        try
        {
            _app.SetAppearance(appearance);
            SetStatus("Apariencia guardada.");
        }
        catch (Exception ex)
        {
            AppearanceBox.SelectedValue = _app.Settings.Appearance;
            SetStatus("No se pudo cambiar la apariencia. " + ex.Message);
        }
    }

    private void LoadSettings()
    {
        var settings = _app.Settings;
        AppearanceBox.SelectedValue = settings.Appearance;
        _pendingShortcut = settings.Shortcut;
        _pendingOcrShortcut = settings.OcrShortcut;
        OcrShortcutBox.Text = settings.OcrShortcut.ToString();
        OcrShortcutSummary.Text = "Extrae texto localmente. " + settings.OcrShortcut;
        ShortcutBox.Text = settings.Shortcut.ToString();
        ShortcutSummary.Text = settings.Shortcut.ToString();
        FolderBox.Text = settings.SaveFolder;
        FormatBox.SelectedIndex = settings.Format == ImageFileFormat.Png ? 0 : 1;
        QualityBox.Text = settings.JpegQuality.ToString();
        QualityBox.IsEnabled = settings.Format == ImageFileFormat.Jpeg;
        DelayBox.Text = settings.CaptureDelaySeconds.ToString();
        CopyBox.IsChecked = settings.CopyAfterCapture;
        AutoSaveBox.IsChecked = settings.AutoSave;
        TrayBox.IsChecked = settings.MinimizeToTray;
        UpdateCheckBox.IsChecked = settings.CheckUpdatesOnStartup;
        FormatSummary.Text = settings.Format == ImageFileFormat.Png ? "Formato de salida · PNG sin pérdida" : $"Formato de salida · JPG, calidad {settings.JpegQuality}";
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(QualityBox.Text, out int quality)) throw new ArgumentException("Introduce una calidad JPG entre 1 y 100.");
            if (!int.TryParse(DelayBox.Text, out int delay)) throw new ArgumentException("Introduce una espera entre 0 y 10 segundos.");
            var settings = _app.Settings with
            {
                Shortcut = _pendingShortcut, OcrShortcut = _pendingOcrShortcut, SaveFolder = FolderBox.Text.Trim(),
                Format = FormatBox.SelectedIndex == 0 ? ImageFileFormat.Png : ImageFileFormat.Jpeg,
                JpegQuality = quality, CaptureDelaySeconds = delay,
                CopyAfterCapture = CopyBox.IsChecked == true, AutoSave = AutoSaveBox.IsChecked == true,
                MinimizeToTray = TrayBox.IsChecked == true, CheckUpdatesOnStartup = UpdateCheckBox.IsChecked == true
            };
            settings.Validate();
            // Comprobar escritura antes de registrar o persistir cambios.
            Directory.CreateDirectory(settings.SaveFolder);
            string probe = Path.Combine(settings.SaveFolder, ".deskshot-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            _app.ApplySettings(settings);
            LoadSettings();
            SetStatus("Configuración guardada.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_app.IsExiting) return;
        e.Cancel = true;
        if (_app.Settings.MinimizeToTray)
        {
            Hide();
            _app.NotifyTrayHidden();
        }
        else _app.ExitApplication();
    }
}
