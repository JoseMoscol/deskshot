namespace DeskShot.Domain;

public enum ImageFileFormat { Png, Jpeg }

public sealed record ShortcutSettings(ModifierKeys Modifiers, Key Key)
{
    public static ShortcutSettings Default => new(ModifierKeys.Control | ModifierKeys.Shift, Key.D2);
    public bool IsValid => Modifiers != ModifierKeys.None &&
        (Modifiers & ~(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows)) == 0 &&
        (Key is >= Key.A and <= Key.Z || Key is >= Key.D0 and <= Key.D9 || Key is >= Key.F1 and <= Key.F24 || Key == Key.PrintScreen);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key is >= Key.D0 and <= Key.D9 ? ((int)Key - (int)Key.D0).ToString() : Key.ToString());
        return string.Join(" + ", parts);
    }
}

public sealed record AppSettings
{
    public string SaveFolder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DeskShot");
    public ImageFileFormat Format { get; init; } = ImageFileFormat.Png;
    public int JpegQuality { get; init; } = 95;
    public ShortcutSettings Shortcut { get; init; } = ShortcutSettings.Default;
    public bool AutoSave { get; init; }
    public bool CopyAfterCapture { get; init; } = true;
    public bool MinimizeToTray { get; init; } = true;
    public int CaptureDelaySeconds { get; init; }

    public AppSettings Validate()
    {
        if (string.IsNullOrWhiteSpace(SaveFolder) || !Path.IsPathFullyQualified(SaveFolder))
            throw new ArgumentException("Selecciona una carpeta de destino con una ruta absoluta.");
        _ = Path.GetFullPath(SaveFolder);
        if (!Enum.IsDefined(Format)) throw new ArgumentException("El formato de imagen no es válido.");
        if (JpegQuality is < 1 or > 100) throw new ArgumentException("La calidad JPG debe estar entre 1 y 100.");
        if (Shortcut is null || !Shortcut.IsValid) throw new ArgumentException("Usa Ctrl, Alt, Shift o Win junto con una letra, número o tecla F.");
        if (CaptureDelaySeconds is < 0 or > 10) throw new ArgumentException("La espera debe estar entre 0 y 10 segundos.");
        return this;
    }
}
