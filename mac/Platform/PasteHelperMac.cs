using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Transkript.Platform;

/// <summary>
/// Copies text to the macOS pasteboard (clipboard) and simulates Cmd+V
/// so the currently focused application receives the transcription.
/// </summary>
public static class PasteHelperMac
{
    // ── AppKit P/Invoke (NSPasteboard) ────────────────────────────────────────
    private const string ObjCLib    = "/usr/lib/libobjc.A.dylib";
    private const string AppKitLib  = "/System/Library/Frameworks/AppKit.framework/AppKit";

    [DllImport(ObjCLib, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);

    [DllImport(ObjCLib, EntryPoint = "sel_registerName")]
    private static extern IntPtr RegisterSelector(string name);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSendPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSendInt(IntPtr receiver, IntPtr selector, int arg1);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern int MsgSendIntRet(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    // ── CoreGraphics P/Invoke (CGEvent for Cmd+V) ────────────────────────────
    private const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(CoreGraphicsLib)]
    private static extern IntPtr CGEventCreateKeyboardEvent(
        IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport(CoreGraphicsLib)]
    private static extern void CGEventSetFlags(IntPtr event_, ulong flags);

    [DllImport(CoreGraphicsLib)]
    private static extern void CGEventPost(uint tap, IntPtr event_);

    [DllImport(CoreGraphicsLib)]
    private static extern void CFRelease(IntPtr cf);

    // macOS virtual key for 'V'
    private const ushort kVK_ANSI_V = 0x09;
    // kCGEventFlagMaskCommand
    private const ulong kCGEventFlagMaskCommand = 0x00100000;
    // kCGHIDEventTap = 0
    private const uint kCGHIDEventTap = 0;

    public static void Paste(string text)
    {
        try
        {
            SetClipboard(text);
            Thread.Sleep(60); // let clipboard settle
            SimulateCmdV();
        }
        catch (Exception ex)
        {
            Logger.Write($"PasteHelperMac.Paste error: {ex.Message}");
            // Fallback: just set clipboard without simulating paste
            try { SetClipboard(text); } catch { }
        }
    }

    private static void SetClipboard(string text)
    {
        // NSPasteboard *pb = [NSPasteboard generalPasteboard];
        IntPtr pbClass = GetClass("NSPasteboard");
        IntPtr genSel  = RegisterSelector("generalPasteboard");
        IntPtr pb      = MsgSend(pbClass, genSel);

        // [pb clearContents]
        IntPtr clearSel = RegisterSelector("clearContents");
        MsgSendInt(pb, clearSel, 0);

        // NSString *str = [NSString stringWithUTF8String: text]
        IntPtr strClass    = GetClass("NSString");
        IntPtr utf8Sel     = RegisterSelector("stringWithUTF8String:");

        // Convert managed string to UTF-8 bytes and pin
        byte[] utf8Bytes = Encoding.UTF8.GetBytes(text + "\0");
        IntPtr nsStr;
        unsafe
        {
            fixed (byte* ptr = utf8Bytes)
                nsStr = MsgSendPtr(strClass, utf8Sel, (IntPtr)ptr);
        }

        // NSString *type = NSPasteboardTypeString
        IntPtr typeClass = GetClass("NSString");
        IntPtr typeSel   = RegisterSelector("stringWithUTF8String:");
        byte[] typeBytes = Encoding.UTF8.GetBytes("public.utf8-plain-text\0");
        IntPtr nsType;
        unsafe
        {
            fixed (byte* ptr = typeBytes)
                nsType = MsgSendPtr(typeClass, typeSel, (IntPtr)ptr);
        }

        // [pb setString:nsStr forType:NSPasteboardTypeString]
        IntPtr setStringSel = RegisterSelector("setString:forType:");
        MsgSendSetString(pb, setStringSel, nsStr, nsType);
    }

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSendSetString(
        IntPtr receiver, IntPtr selector, IntPtr str, IntPtr type);

    private static void SimulateCmdV()
    {
        // Key down: V with Command modifier
        IntPtr keyDown = CGEventCreateKeyboardEvent(IntPtr.Zero, kVK_ANSI_V, true);
        CGEventSetFlags(keyDown, kCGEventFlagMaskCommand);
        CGEventPost(kCGHIDEventTap, keyDown);
        CFRelease(keyDown);

        Thread.Sleep(20);

        // Key up: V with Command modifier
        IntPtr keyUp = CGEventCreateKeyboardEvent(IntPtr.Zero, kVK_ANSI_V, false);
        CGEventSetFlags(keyUp, kCGEventFlagMaskCommand);
        CGEventPost(kCGHIDEventTap, keyUp);
        CFRelease(keyUp);
    }
}
