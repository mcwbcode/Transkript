using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Transkript.Platform;

/// <summary>
/// Registers a system-wide hotkey via macOS Carbon RegisterEventHotKey.
/// Uses [UnmanagedCallersOnly] for the Carbon callback — safe on arm64/.NET 8.
/// </summary>
public sealed unsafe class GlobalHotkeyMac : IDisposable
{
    // ── Carbon P/Invoke ───────────────────────────────────────────────────────
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    [DllImport(Carbon)]
    private static extern int RegisterEventHotKey(
        uint keyCode, uint modifiers, EventHotKeyID id,
        IntPtr target, uint options, out IntPtr outRef);

    [DllImport(Carbon)]
    private static extern int UnregisterEventHotKey(IntPtr inRef);

    [DllImport(Carbon)]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport(Carbon)]
    private static extern int InstallEventHandler(
        IntPtr target, IntPtr handler, uint numTypes,
        IntPtr specs,   // passed as fixed pointer — LPArray marshaling unreliable on arm64
        IntPtr userData, out IntPtr outRef);

    [DllImport(Carbon)]
    private static extern int RemoveEventHandler(IntPtr inRef);

    [DllImport(Carbon)]
    private static extern uint GetEventKind(IntPtr eventRef);

    [DllImport(Carbon)]
    private static extern int GetEventParameter(
        IntPtr eventRef, uint paramName, uint desiredType,
        IntPtr outActualType, uint bufferSize, IntPtr outActualSize,
        out EventHotKeyID outData);

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyID { public uint signature; public uint id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec { public uint eventClass; public uint eventKind; }

    // Carbon constants
    private const uint kEventClassKeyboard     = 0x6B657962; // 'keyb'
    private const uint kEventHotKeyPressed    = 5;
    private const uint kEventHotKeyReleased   = 6;
    private const uint kEventParamDirectObject = 0x2D2D2D2D; // '----'
    private const uint typeEventHotKeyID      = 0x686B6579; // 'hkey'
    private const uint typeWildCard           = 0x2A2A2A2A; // '****'
    private const uint OurSignature           = 0x54524E53; // 'TRNS'
    private const uint OurHotkeyId           = 1;

    // ── macOS Virtual Key Codes ───────────────────────────────────────────────
    public const uint VK_F13       = 105;
    public const uint VK_F14       = 107;
    public const uint VK_F15       = 113;
    public const uint VK_F16       = 106;
    public const uint VK_F17       = 64;
    public const uint VK_F18       = 79;
    public const uint VK_F19       = 80;
    public const uint VK_CAPS_LOCK = 57;
    public const uint VK_RIGHT_CMD = 54;

    // macOS modifier flags (Carbon)
    public const uint cmdKey     = 0x0100;
    public const uint shiftKey   = 0x0200;
    public const uint optionKey  = 0x0800;
    public const uint controlKey = 0x1000;

    // ── Static state (one instance per process) ───────────────────────────────
    private static GlobalHotkeyMac? _instance;
    private static bool _s_keyDown;

    // ── Instance state ────────────────────────────────────────────────────────
    private IntPtr _hotKeyRef  = IntPtr.Zero;
    private IntPtr _handlerRef = IntPtr.Zero;
    private bool   _disposed;

    public uint HotkeyCode      { get; set; } = VK_F13;
    public uint HotkeyModifiers { get; set; } = 0;

    public event Action? KeyPressed;
    public event Action? KeyReleased;

    public void Install()
    {
        _instance  = this;
        _s_keyDown = false;

        var specs = stackalloc EventTypeSpec[2];
        specs[0] = new EventTypeSpec { eventClass = kEventClassKeyboard, eventKind = kEventHotKeyPressed  };
        specs[1] = new EventTypeSpec { eventClass = kEventClassKeyboard, eventKind = kEventHotKeyReleased };

        IntPtr target = GetApplicationEventTarget();

        // Install using [UnmanagedCallersOnly] function pointer — arm64 safe
        // Pass specs as raw pointer to avoid LPArray marshaling issues on arm64
        int err = InstallEventHandler(
            target,
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int>)&CbHotkey,
            2,
            (IntPtr)specs,
            IntPtr.Zero,
            out _handlerRef);

        if (err != 0)
            Logger.Write($"GlobalHotkeyMac: InstallEventHandler error {err}");

        var hkId = new EventHotKeyID { signature = OurSignature, id = OurHotkeyId };
        err = RegisterEventHotKey(HotkeyCode, HotkeyModifiers, hkId, target, 0, out _hotKeyRef);

        if (err != 0)
            Logger.Write($"GlobalHotkeyMac: RegisterEventHotKey error {err}");
        else
            Logger.Write($"GlobalHotkeyMac: Hotkey registered (code={HotkeyCode}, mods={HotkeyModifiers})");
    }

    // ── Carbon callback — [UnmanagedCallersOnly] prevents exception-unwind crash ──

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CbHotkey(IntPtr callRef, IntPtr eventRef, IntPtr userData)
    {
        try
        {
            // We have exactly one registered hotkey, so any kEventHotKeyPressed /
            // kEventHotKeyReleased reaching this handler is ours.
            // GetEventParameter returns errAECoercionFail (-1700) on arm64/macOS 26
            // regardless of the type constant used — skip it entirely.
            uint kind = GetEventKind(eventRef);

            if (kind == kEventHotKeyPressed && !_s_keyDown)
            {
                _s_keyDown = true;
                _instance?.KeyPressed?.Invoke();
            }
            else if (kind == kEventHotKeyReleased && _s_keyDown)
            {
                _s_keyDown = false;
                _instance?.KeyReleased?.Invoke();
            }
        }
        catch (Exception ex)
        {
            // Must never throw out of [UnmanagedCallersOnly]
            try { Logger.Write($"GlobalHotkeyMac callback error: {ex.GetType().Name}: {ex.Message}"); } catch { }
        }

        return 0; // noErr
    }

    /// <summary>
    /// Re-registers the hotkey after HotkeyCode/HotkeyModifiers have been changed.
    /// The event handler stays installed; only the Carbon hotkey binding is swapped.
    /// </summary>
    public void Reinstall()
    {
        if (_hotKeyRef != IntPtr.Zero)
        {
            UnregisterEventHotKey(_hotKeyRef);
            _hotKeyRef = IntPtr.Zero;
        }

        IntPtr target = GetApplicationEventTarget();
        var hkId = new EventHotKeyID { signature = OurSignature, id = OurHotkeyId };
        int err = RegisterEventHotKey(HotkeyCode, HotkeyModifiers, hkId, target, 0, out _hotKeyRef);

        if (err != 0)
            Logger.Write($"GlobalHotkeyMac: Reinstall error {err}");
        else
            Logger.Write($"GlobalHotkeyMac: Hotkey re-registered (code={HotkeyCode}, mods={HotkeyModifiers})");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_instance == this) _instance = null;

        if (_hotKeyRef != IntPtr.Zero)
        {
            UnregisterEventHotKey(_hotKeyRef);
            _hotKeyRef = IntPtr.Zero;
        }
        if (_handlerRef != IntPtr.Zero)
        {
            RemoveEventHandler(_handlerRef);
            _handlerRef = IntPtr.Zero;
        }
    }
}
