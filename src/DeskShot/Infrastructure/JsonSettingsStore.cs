using System.Text.Json;
using System.Text.Json.Serialization;
using DeskShot.Application;
using DeskShot.Domain;

namespace DeskShot.Infrastructure;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string? LoadWarning { get; private set; }

    public JsonSettingsStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskShot", "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(_path)) return new AppSettings();
        try
        {
            string json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            using var document = JsonDocument.Parse(json);
            // Las preferencias anteriores no tenían OCR: conservar su atajo de captura aunque usara Ctrl+Shift+3.
            if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty(nameof(AppSettings.OcrShortcut), out _) && settings.Shortcut == settings.OcrShortcut)
                settings = settings with { OcrShortcut = new(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, Key.D3) };
            return settings.Validate();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            LoadWarning = "No se pudo leer la configuración. Se usarán los valores iniciales. El archivo original se conserva.";
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
