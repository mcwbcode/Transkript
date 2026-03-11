using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Transkript;

/// <summary>
/// Installs a WH_KEYBOARD_LL hook and fires events when the configured hotkey is pressed/released.
/// Must be installed on a thread with a message pump (the WPF UI thread).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private IntPtr _hookHandle = IntPtr.Zero;

    // Keep the delegate alive so GC doesn't collect it while the hook is active.
    private readonly NativeMethods.LowLevelKeyboardProc _proc;

    private bool _keyDown = false;

    /// <summary>Virtual-key code of the hotkey. Can be changed at runtime.</summary>
    public int HotkeyVk { get; set; } = NativeMethods.VK_RCONTROL;

    public event Action? KeyPressed;
    public event Action? KeyReleased;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        using var process = Process.GetCurrentProcess();
        using var module  = process.MainModule!;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(module.ModuleName),
            0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kb  = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;

            if ((int)kb.vkCode == HotkeyVk)
            {
                bool isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
                bool isUp   = msg == NativeMethods.WM_KEYUP   || msg == NativeMethods.WM_SYSKEYUP;

                if (isDown && !_keyDown)
                {
                    _keyDown = true;
                    KeyPressed?.Invoke();
                }
                else if (isUp && _keyDown)
                {
                    _keyDown = false;
                    KeyReleased?.Invoke();
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
