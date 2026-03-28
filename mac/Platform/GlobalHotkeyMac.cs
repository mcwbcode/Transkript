using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Transkript.Platform;

/// <summary>
/// Registers a system-wide hotkey on macOS using the Carbon Event Manager API
/// (RegisterEventHotKey). Works without accessibility permissions.
///
/// The hotkey is polled via a background thread that calls GetNextEventMatchingMask.
/// </summary>
public sealed class GlobalHotkeyMac : IDisposable
{
    // ── Carbon P/Invoke ───────────────────────────────────────────────────────
    private const string CarbonLib = "/System/Library/Frameworks/Carbon.framework/Carbon";

    [DllImport(CarbonLib)]
    private static extern int RegisterEventHotKey(
        uint inHotKeyCode,
        uint inHotKeyModifiers,
        EventHotKeyID inHotKeyID,
        IntPtr inTarget,
        uint inOptions,
        out IntPtr outRef);

    [DllImport(CarbonLib)]
    private static extern int UnregisterEventHotKey(IntPtr inHotKey);

    [DllImport(CarbonLib)]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport(CarbonLib)]
    private static extern int InstallEventHandler(
        IntPtr inTarget,
        IntPtr inHandler,
        uint inNumTypes,
        [MarshalAs(UnmanagedType.LPArray)] EventTypeSpec[] inList,
        IntPtr inUserData,
        out IntPtr outRef);

    [DllImport(CarbonLib)]
    private static extern int RemoveEventHandler(IntPtr inRef);

    [DllImport(CarbonLib)]
    private static extern int GetEventParameter(
        IntPtr inEvent,
        uint inName,
        uint inDesiredType,
        IntPtr outActualType,
        uint inBufferSize,
        IntPtr outActualSize,
        out EventHotKeyID outData);

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyID
    {
        public uint signature;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint eventClass;
        public uint eventKind;
    }

    // Carbon constants
    private const uint kEventClassKeyboard = 0x6B657962; // 'keyb'
    private const uint kEventHotKeyPressed = 5;
    private const uint kEventHotKeyReleased = 6;
    private const uint kEventParamDirectObject = 0x2D2D2D2D; // '----'
    private const uint typeEventHotKeyID = 0x686B6579; // 'hkey'

    // ── macOS Virtual Key Codes ───────────────────────────────────────────────
    // Common keys for use as hotkeys
    public const uint VK_F13       = 105;
    public const uint VK_F14       = 107;
    public const uint VK_F15       = 113;
    public const uint VK_F16       = 106;
    public const uint VK_F17       = 64;
    public const uint VK_F18       = 79;
    public const uint VK_F19       = 80;
    public const uint VK_CAPS_LOCK = 57;  // CapsLock (good default for dictation)
    public const uint VK_RIGHT_CMD = 54;

    // macOS modifier keys (Carbon)
    public const uint cmdKey      = 0x0100;
    public const uint shiftKey    = 0x0200;
    public const uint optionKey   = 0x0800;
    public const uint controlKey  = 0x1000;

    // ── State ────────────────────────────────────────────────────────────────
    private IntPtr _hotKeyRef      = IntPtr.Zero;
    private IntPtr _handlerRef     = IntPtr.Zero;
    private bool   _keyDown        = false;
    private bool   _disposed       = false;

    // Delegate must be kept alive
    private readonly CarbonEventHandlerDelegate _handlerDelegate;
    private delegate int CarbonEventHandlerDelegate(
        IntPtr callRef, IntPtr eventRef, IntPtr userData);

    /// <summary>macOS virtual key code for the hotkey. Default: F13 (good dictation key).</summary>
    public uint HotkeyCode { get; set; } = VK_F13;

    /// <summary>macOS modifier flags. Default: 0 (no modifier).</summary>
    public uint HotkeyModifiers { get; set; } = 0;

    public event Action? KeyPressed;
    public event Action? KeyReleased;

    public GlobalHotkeyMac()
    {
        _handlerDelegate = EventHandler;
    }

    public void Install()
    {
        var pressSpec   = new EventTypeSpec { eventClass = kEventClassKeyboard, eventKind = kEventHotKeyPressed  };
        var releaseSpec = new EventTypeSpec { eventClass = kEventClassKeyboard, eventKind = kEventHotKeyReleased };

        IntPtr target = GetApplicationEventTarget();

        // Install event handler for both press and release
        var specs = new[] { pressSpec, releaseSpec };
        int err = InstallEventHandler(
            target,
            Marshal.GetFunctionPointerForDelegate(_handlerDelegate),
            (uint)specs.Length,
            specs,
            IntPtr.Zero,
            out _handlerRef);

        if (err != 0)
            Logger.Write($"GlobalHotkeyMac: InstallEventHandler error {err}");

        // Register the hotkey
        var hotKeyId = new EventHotKeyID { signature = 0x54524E53, id = 1 }; // 'TRNS'
        err = RegisterEventHotKey(HotkeyCode, HotkeyModifiers, hotKeyId, target, 0, out _hotKeyRef);

        if (err != 0)
            Logger.Write($"GlobalHotkeyMac: RegisterEventHotKey error {err}");
        else
            Logger.Write($"GlobalHotkeyMac: Hotkey registered (code={HotkeyCode}, mods={HotkeyModifiers})");
    }

    private int EventHandler(IntPtr callRef, IntPtr eventRef, IntPtr userData)
    {
        // Get the event class/kind
        // We need to figure out if it's press or release by checking which handler fired
        // We do this by checking the event kind via GetEventKind
        uint kind = GetEventKind(eventRef);

        try
        {
            if (kind == kEventHotKeyPressed && !_keyDown)
            {
                _keyDown = true;
                KeyPressed?.Invoke();
            }
            else if (kind == kEventHotKeyReleased && _keyDown)
            {
                _keyDown = false;
                KeyReleased?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Logger.Write($"GlobalHotkeyMac EventHandler: {ex.Message}");
        }

        return 0; // noErr
    }

    [DllImport(CarbonLib)]
    private static extern uint GetEventKind(IntPtr eventRef);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

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
