using DeskShot.Domain;

namespace DeskShot.Infrastructure;

// Reutiliza las teclas ya registradas (incluido intercambiarlas), y prepara las nuevas antes de guardar.
public sealed class CaptureHotkeys(nint hwnd) : IDisposable
{
    private readonly Dictionary<ShortcutSettings, GlobalHotkey> _bindings = [];
    public ShortcutSettings? Capture { get; private set; }
    public ShortcutSettings? Ocr { get; private set; }
    public event EventHandler? CapturePressed;
    public event EventHandler? OcrPressed;
    public void Apply(ShortcutSettings? capture, ShortcutSettings? ocr, Action? persist = null)
    {
        if (capture is not null && !capture.IsValid || ocr is not null && !ocr.IsValid || capture is not null && capture == ocr)
            throw new ArgumentException("Elige dos atajos válidos y diferentes.");
        var wanted = new[] { capture, ocr }.Where(s => s is not null).Cast<ShortcutSettings>().ToArray();
        var added = new Dictionary<ShortcutSettings, GlobalHotkey>();
        var slots = new Dictionary<GlobalHotkey, int>();
        // Cuatro pares de identificadores permiten dos registros activos y dos provisionales.
        try
        {
            foreach (var shortcut in wanted.Where(s => !_bindings.ContainsKey(s)))
            {
                int id = Enumerable.Range(0, 4).Select(i => 0x531 + i * 0x10).First(candidate => !_ids.Values.Contains(candidate) && !slots.Values.Contains(candidate));
                var binding = new GlobalHotkey(hwnd, id);
                try { binding.Register(shortcut); } catch { binding.Dispose(); throw; }
                binding.Pressed += (_, _) => { if (binding.Current == Capture) CapturePressed?.Invoke(this, EventArgs.Empty); else if (binding.Current == Ocr) OcrPressed?.Invoke(this, EventArgs.Empty); };
                added.Add(shortcut, binding); slots.Add(binding, id);
            }
            persist?.Invoke();
        }
        catch { foreach (var binding in added.Values) binding.Dispose(); throw; }
        Capture = capture; Ocr = ocr;
        foreach (var shortcut in _bindings.Keys.Where(s => !wanted.Contains(s)).ToArray())
        { var binding = _bindings[shortcut]; _ids.Remove(binding); binding.Dispose(); _bindings.Remove(shortcut); }
        foreach (var entry in added) { _bindings.Add(entry.Key, entry.Value); _ids.Add(entry.Value, slots[entry.Value]); }
    }
    private readonly Dictionary<GlobalHotkey, int> _ids = [];
    public void Dispose() { foreach (var binding in _bindings.Values) binding.Dispose(); _bindings.Clear(); _ids.Clear(); }
}
