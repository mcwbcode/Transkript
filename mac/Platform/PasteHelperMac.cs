using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Transkript.Platform;

/// <summary>
/// Copies text to the macOS pasteboard (clipboard) and simulates Cmd+V
/// so the previously focused application receives the transcription.
/// </summary>
public static class PasteHelperMac
{
    // ── AppKit P/Invoke (NSPasteboard + NSWorkspace) ──────────────────────────
    private const string ObjCLib         = "/usr/lib/libobjc.A.dylib";
    private const string AppKitLib       = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string AppServicesLib  = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(ObjCLib, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);

    [DllImport(ObjCLib, EntryPoint = "objc_retain")]
    private static extern IntPtr ObjcRetain(IntPtr obj);

    [DllImport(ObjCLib, EntryPoint = "objc_release")]
    private static extern void ObjcRelease(IntPtr obj);

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

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern void MsgSendNUInt(IntPtr receiver, IntPtr selector, nuint arg1);

    // ── Accessibility (TCC) ───────────────────────────────────────────────────
    [DllImport(AppServicesLib)]
    private static extern bool AXIsProcessTrusted();

    // ── CoreGraphics (CGEvent for Cmd+V) ─────────────────────────────────────
    [DllImport(CoreGraphicsLib)]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport(CoreGraphicsLib)]
    private static extern void CGEventSetFlags(IntPtr event_, ulong flags);

    [DllImport(CoreGraphicsLib)]
    private static extern void CGEventPost(uint tap, IntPtr event_);

    [DllImport(CoreGraphicsLib)]
    private static extern void CFRelease(IntPtr cf);

    private const ushort kVK_ANSI_V            = 0x09;
    private const ulong  kCGEventFlagMaskCommand = 0x00100000;
    private const uint   kCGHIDEventTap          = 0;
    private const nuint  NSApplicationActivateIgnoringOtherApps = 2;

    public static bool IsAccessibilityGranted() => AXIsProcessTrusted();

    // ── Frontmost app tracking ────────────────────────────────────────────────
    private static IntPtr _savedFrontmostApp = IntPtr.Zero;

    /// <summary>
    /// Call this before recording starts to save which app should receive the paste.
    /// </summary>
    public static void SaveFrontmostApp()
    {
        try
        {
            // Release any previously saved app
            if (_savedFrontmostApp != IntPtr.Zero)
            {
                ObjcRelease(_savedFrontmostApp);
                _savedFrontmostApp = IntPtr.Zero;
            }

            IntPtr workspace = MsgSend(GetClass("NSWorkspace"), RegisterSelector("sharedWorkspace"));
            IntPtr app = MsgSend(workspace, RegisterSelector("frontmostApplication"));
            if (app != IntPtr.Zero)
                _savedFrontmostApp = ObjcRetain(app); // retain to survive autorelease pool drain
            Logger.Write("SaveFrontmostApp : OK");
        }
        catch (Exception ex)
        {
            Logger.Write($"SaveFrontmostApp error: {ex.Message}");
            _savedFrontmostApp = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Returns the bundle identifier of the saved frontmost app, e.g. "com.apple.TextEdit".
    /// </summary>
    private static string? GetSavedAppBundleId()
    {
        if (_savedFrontmostApp == IntPtr.Zero) return null;
        try
        {
            IntPtr nsStr = MsgSend(_savedFrontmostApp, RegisterSelector("bundleIdentifier"));
            if (nsStr == IntPtr.Zero) return null;
            IntPtr ptr = MsgSend(nsStr, RegisterSelector("UTF8String"));
            return ptr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(ptr);
        }
        catch (Exception ex)
        {
            Logger.Write($"GetSavedAppBundleId error: {ex.Message}");
            return null;
        }
    }

    public static void ActivateSavedApp()
    {
        if (_savedFrontmostApp == IntPtr.Zero) return;
        try
        {
            MsgSendNUInt(_savedFrontmostApp, RegisterSelector("activateWithOptions:"),
                         NSApplicationActivateIgnoringOtherApps);
            Logger.Write("ActivateSavedApp : OK");
        }
        catch (Exception ex)
        {
            Logger.Write($"ActivateSavedApp error: {ex.Message}");
        }
    }

    public static void Paste(string text)
    {
        // Read bundle ID before releasing the retained object
        string? bundleId = GetSavedAppBundleId();

        if (_savedFrontmostApp != IntPtr.Zero)
        {
            ObjcRelease(_savedFrontmostApp);
            _savedFrontmostApp = IntPtr.Zero;
        }

        Logger.Write($"Paste : bundleId={bundleId ?? "(none)"}");

        try
        {
            SetClipboard(text);
            Thread.Sleep(60); // let clipboard settle
            SimulatePaste(bundleId);
        }
        catch (Exception ex)
        {
            Logger.Write($"PasteHelperMac.Paste error: {ex.Message}");
            // Fallback: clipboard is already set, user can paste manually
            try { SetClipboard(text); } catch { }
        }
    }

    /// <summary>
    /// Activates the target app by bundle ID and sends Cmd+V via CGEventPost.
    /// Requires Accessibility permission — opens System Preferences if not granted.
    /// </summary>
    private static void SimulatePaste(string? bundleId)
    {
        if (!AXIsProcessTrusted())
        {
            Logger.Write("Accessibility non accordée — ouverture Réglages");
            // Open directly to Privacy > Accessibility
            System.Diagnostics.Process.Start("open",
                "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility");
            return;
        }

        // Activate the saved app by bundle ID via NSRunningApplication
        if (!string.IsNullOrEmpty(bundleId))
        {
            try
            {
                // [NSRunningApplication runningApplicationsWithBundleIdentifier:]
                // Returns NSArray; we take firstObject
                IntPtr cls      = GetClass("NSRunningApplication");
                IntPtr sel      = RegisterSelector("runningApplicationsWithBundleIdentifier:");
                byte[] bidBytes = Encoding.UTF8.GetBytes(bundleId + "\0");
                IntPtr nsStr;
                unsafe { fixed (byte* p = bidBytes) nsStr = MsgSendPtr(GetClass("NSString"), RegisterSelector("stringWithUTF8String:"), (IntPtr)p); }
                IntPtr arr     = MsgSendPtr(cls, sel, nsStr);
                IntPtr appInst = MsgSend(arr, RegisterSelector("firstObject"));
                if (appInst != IntPtr.Zero)
                {
                    MsgSendNUInt(appInst, RegisterSelector("activateWithOptions:"),
                                 NSApplicationActivateIgnoringOtherApps);
                    Thread.Sleep(120);
                }
            }
            catch (Exception ex) { Logger.Write($"Activate error: {ex.Message}"); }
        }

        // Post Cmd+V
        IntPtr keyDown = CGEventCreateKeyboardEvent(IntPtr.Zero, kVK_ANSI_V, true);
        CGEventSetFlags(keyDown, kCGEventFlagMaskCommand);
        CGEventPost(kCGHIDEventTap, keyDown);
        CFRelease(keyDown);
        Thread.Sleep(20);
        IntPtr keyUp = CGEventCreateKeyboardEvent(IntPtr.Zero, kVK_ANSI_V, false);
        CGEventSetFlags(keyUp, kCGEventFlagMaskCommand);
        CGEventPost(kCGHIDEventTap, keyUp);
        CFRelease(keyUp);
        Logger.Write("SimulatePaste : Cmd+V envoyé");
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
}
