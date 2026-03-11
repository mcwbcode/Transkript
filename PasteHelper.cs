using System.Threading;
using Clipboard = System.Windows.Clipboard;

namespace Transkript;

/// <summary>
/// Copies text to the clipboard and simulates Ctrl+V so the currently focused
/// application receives the transcription.
/// </summary>
public static class PasteHelper
{
    public static void Paste(string text)
    {
        // Set clipboard (must be called from STA/UI thread)
        Clipboard.SetText(text);

        // Brief pause so the clipboard contents settle before the keystroke
        Thread.Sleep(60);

        // Simulate Ctrl+V
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_V,       0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_V,       0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
