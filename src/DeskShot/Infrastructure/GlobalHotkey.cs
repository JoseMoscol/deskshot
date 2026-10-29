using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using DeskShot.Domain;

namespace DeskShot.Infrastructure;

public sealed class GlobalHotkey : IDisposable
{
    private readonly HwndSource _source;
    private int _activeId;
    private readonly int _idBase;
    private bool _registered;
    public event EventHandler? Pressed;
    public ShortcutSettings? Current { get; private set; }

    public GlobalHotkey(nint hwnd, int idBase = 0x531)
    {
        _idBase = idBase;
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("La ventana todavía no está lista.");
        _source.AddHook(WindowProcedure);
    }

    public void Register(ShortcutSettings shortcut)
    {
        if (!shortcut.IsValid) throw new ArgumentException("El atajo no es válido.");
        if (_registered && shortcut == Current) return;
        // Registrar primero el nuevo: un conflicto no elimina el atajo anterior.
        int candidateId = _activeId == _idBase ? _idBase + 1 : _idBase;
        uint modifiers = 0x4000; // MOD_NOREPEAT
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 1;
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 2;
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 4;
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= 8;
        if (!NativeMethods.RegisterHotKey(_source.Handle, candidateId, modifiers, (uint)KeyInterop.VirtualKeyFromKey(shortcut.Key)))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Ese atajo está reservado o lo está usando otra aplicación. Prueba otra combinación.");
        if (_registered) NativeMethods.UnregisterHotKey(_source.Handle, _activeId);
        _activeId = candidateId;
        _registered = true;
        Current = shortcut;
    }

    private nint WindowProcedure(nint hwnd, int message, nint wparam, nint lparam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && (int)wparam == _activeId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return 0;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WindowProcedure);
    }

    public void Unregister()
    {
        if (_registered) NativeMethods.UnregisterHotKey(_source.Handle, _activeId);
        _registered = false;
        Current = null;
    }
}
