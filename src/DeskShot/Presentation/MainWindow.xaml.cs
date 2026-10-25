using System.ComponentModel;
using System.Diagnostics;
using DeskShot.Domain;

namespace DeskShot.Presentation;

public partial class MainWindow : Window
{
    private readonly App _app;
    private ShortcutSettings _pendingShortcut = ShortcutSettings.Default;

    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        LoadSettings();
        Closing += OnClosing;
    }

    public void SetStatus(string message) => StatusText.Text = message;
    public void SetBusy(bool busy) { RegionButton.IsEnabled = DesktopButton.IsEnabled = ScrollButton.IsEnabled = UpdatesButton.IsEnabled = !busy; }
    public bool IsEditingShortcut => IsVisible && ShortcutBox.IsKeyboardFocusWithin;

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
        HomeNav.Background = new SolidColorBrush(settings ? Colors.Transparent : Color.FromRgb(38, 56, 74));
        SettingsNav.Background = new SolidColorBrush(settings ? Color.FromRgb(38, 56, 74) : Colors.Transparent);
    }

    private async void CaptureRegion_Click(object sender, RoutedEventArgs e) => await _app.CaptureAsync(true);
    private async void CaptureDesktop_Click(object sender, RoutedEventArgs e) => await _app.CaptureAsync(false);
    private async void CaptureScroll_Click(object sender, RoutedEventArgs e) => await _app.CaptureAsync(true, scrolling: true);
    private void Exit_Click(object sender, RoutedEventArgs e) => _app.ExitApplication();
    private void Updates_Click(object sender, RoutedEventArgs e) => _app.ShowUpdates();

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
        _pendingShortcut = shortcut;
        ShortcutBox.Text = shortcut.ToString();
        SetStatus("Pulsa Guardar configuración para aplicar el nuevo atajo.");
    }

    private void FormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QualityBox is not null) QualityBox.IsEnabled = FormatBox.SelectedIndex == 1;
    }

    private void LoadSettings()
    {
        var settings = _app.Settings;
        _pendingShortcut = settings.Shortcut;
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
        FormatSummary.Text = settings.Format == ImageFileFormat.Png ? "PNG · sin pérdida" : $"JPG · calidad {settings.JpegQuality}";
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(QualityBox.Text, out int quality)) throw new ArgumentException("Introduce una calidad JPG entre 1 y 100.");
            if (!int.TryParse(DelayBox.Text, out int delay)) throw new ArgumentException("Introduce una espera entre 0 y 10 segundos.");
            var settings = new AppSettings
            {
                Shortcut = _pendingShortcut, SaveFolder = FolderBox.Text.Trim(),
                Format = FormatBox.SelectedIndex == 0 ? ImageFileFormat.Png : ImageFileFormat.Jpeg,
                JpegQuality = quality, CaptureDelaySeconds = delay,
                CopyAfterCapture = CopyBox.IsChecked == true, AutoSave = AutoSaveBox.IsChecked == true,
                MinimizeToTray = TrayBox.IsChecked == true, CheckUpdatesOnStartup = UpdateCheckBox.IsChecked == true
            }.Validate();
            // Comprobar escritura antes de registrar o persistir cambios.
            Directory.CreateDirectory(settings.SaveFolder);
            string probe = Path.Combine(settings.SaveFolder, ".deskshot-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            _app.ApplySettings(settings);
            LoadSettings();
            SetStatus("Configuración guardada. El atajo ya está activo.");
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
