using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;

namespace Transkript.Platform;

/// <summary>
/// macOS menu bar icon via native NSStatusBar/NSMenu P/Invoke.
/// Uses [UnmanagedCallersOnly] static methods for ObjC callbacks (arm64 safe).
/// </summary>
public sealed unsafe class MenuBarMac : IDisposable
{
    public event Action? ExitRequested;
    public event Action? SettingsRequested;
    public event Action? HistoryRequested;
    public event Action? UpdateRequested;
    public event Action? RecordRequested;
    public event Action? RecordStopRequested;
    // (test hooks — kept for future use)

    // ── ObjC P/Invoke ─────────────────────────────────────────────────────────
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Sel(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr obj, IntPtr sel);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendP(IntPtr obj, IntPtr sel, IntPtr a);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPPP(IntPtr obj, IntPtr sel, IntPtr a, IntPtr b, IntPtr c);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendD(IntPtr obj, IntPtr sel, double a);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidP(IntPtr obj, IntPtr sel, IntPtr a);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidB(IntPtr obj, IntPtr sel, bool a);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidII(IntPtr obj, IntPtr sel, nint a, nint b);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidPI(IntPtr obj, IntPtr sel, IntPtr a, nint b);

    // ObjC class building
    [DllImport(ObjC)]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extra);

    [DllImport(ObjC)]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(ObjC)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

    // ── Static instance registry (one per process) ────────────────────────────
    // ObjC callbacks must be static; we route through the singleton instance.
    private static MenuBarMac? _instance;

    // ── State ─────────────────────────────────────────────────────────────────
    private IntPtr _statusItem;
    private IntPtr _menu;
    private IntPtr _statusMenuItem;
    private IntPtr _updateMenuItem;
    private bool   _disposed;

    private const double NSVariableStatusItemLength = -1.0;

    public MenuBarMac(string appName)
    {
        _instance = this;

        RegisterTargetClass();

        IntPtr targetCls = GetClass("TranskriptMenuTarget");
        IntPtr target    = Send(Send(targetCls, Sel("alloc")), Sel("init"));

        // Create status item
        IntPtr statusBar = Send(GetClass("NSStatusBar"), Sel("systemStatusBar"));
        _statusItem = SendD(statusBar, Sel("statusItemWithLength:"), NSVariableStatusItemLength);
        Send(_statusItem, Sel("retain"));

        SetButtonIcon();
        _menu = BuildMenu(appName, target);
        SendVoidP(_statusItem, Sel("setMenu:"), _menu);
    }

    // ── Button icon ───────────────────────────────────────────────────────────

    private void SetButtonIcon()
    {
        IntPtr btn = Send(_statusItem, Sel("button"));
        if (btn == IntPtr.Zero) return;

        // Use the dedicated menu bar template PNG (transparent background, black bars)
        string iconPath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "Assets", "menubar_icon.png");

        bool iconSet = false;
        if (System.IO.File.Exists(iconPath))
        {
            try
            {
                IntPtr nsPath = NSString(iconPath);
                IntPtr img    = SendP(
                    Send(GetClass("NSImage"), Sel("alloc")),
                    Sel("initWithContentsOfFile:"), nsPath);

                if (img != IntPtr.Zero)
                {
                    // setSize: CGSize(18,18) — standard menu bar icon size
                    SetImageSize(img, 18, 18);
                    SendVoidB(img, Sel("setTemplate:"), true); // adapts to dark/light mode
                    SendVoidP(btn, Sel("setImage:"), img);
                    iconSet = true;
                }
            }
            catch (Exception ex) { Logger.Write($"MenuBarMac icon: {ex.Message}"); }
        }

        if (!iconSet)
            SendVoidP(btn, Sel("setTitle:"), NSString("⌨"));
    }

    // CGSize is two doubles on arm64
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidDD(IntPtr obj, IntPtr sel, double w, double h);

    private static void SetImageSize(IntPtr img, double w, double h)
        => SendVoidDD(img, Sel("setSize:"), w, h);

    // ── Menu builder ──────────────────────────────────────────────────────────

    private IntPtr BuildMenu(string appName, IntPtr target)
    {
        IntPtr menu = Send(Send(GetClass("NSMenu"), Sel("alloc")), Sel("init"));
        Send(menu, Sel("retain"));
        // Don't auto-enable items
        SendVoidB(menu, Sel("setAutoenablesItems:"), false);

        AddItem(menu, appName,       IntPtr.Zero, null,               false);
        AddSeparator(menu);
        _statusMenuItem = AddItem(menu, "Chargement…", IntPtr.Zero, null, false);
        AddSeparator(menu);
        AddSeparator(menu);
        AddItem(menu, "Paramètres…", target, "onSettings:");
        AddItem(menu, "Historique",  target, "onHistory:");
        AddSeparator(menu);
        AddItem(menu, "Quitter",     target, "onQuit:");

        return menu;
    }

    private static IntPtr AddItem(IntPtr menu, string title, IntPtr target, string? action, bool enabled = true)
    {
        IntPtr item = SendPPP(
            Send(GetClass("NSMenuItem"), Sel("alloc")),
            Sel("initWithTitle:action:keyEquivalent:"),
            NSString(title),
            action != null ? Sel(action) : IntPtr.Zero,
            NSString(""));

        SendVoidP(item, Sel("setTarget:"), target);
        SendVoidB(item, Sel("setEnabled:"), enabled);
        SendVoidP(menu, Sel("addItem:"), item);
        return item;
    }

    private static void AddSeparator(IntPtr menu)
        => SendVoidP(menu, Sel("addItem:"),
               Send(GetClass("NSMenuItem"), Sel("separatorItem")));

    // ── ObjC target class (registered once per process) ───────────────────────

    private static bool _classRegistered;
    private static readonly object _classLock = new();

    private static void RegisterTargetClass()
    {
        lock (_classLock)
        {
            if (_classRegistered) return;

            IntPtr cls = objc_allocateClassPair(GetClass("NSObject"), "TranskriptMenuTarget", 0);

            class_addMethod(cls, Sel("onRecord:"),
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&CbRecord, "v@:@");
            class_addMethod(cls, Sel("onStopRecord:"),
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&CbStopRecord, "v@:@");
            class_addMethod(cls, Sel("onSettings:"),
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&CbSettings, "v@:@");
            class_addMethod(cls, Sel("onHistory:"),
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&CbHistory, "v@:@");
            class_addMethod(cls, Sel("onUpdate:"),
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&CbUpdate, "v@:@");
            class_addMethod(cls, Sel("onQuit:"),
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&CbQuit, "v@:@");

            objc_registerClassPair(cls);
            _classRegistered = true;
        }
    }

    // Static ObjC callbacks — arm64 safe via UnmanagedCallersOnly
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CbRecord(IntPtr self, IntPtr sel, IntPtr sender)
        => Dispatcher.UIThread.Post(() => _instance?.RecordRequested?.Invoke());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CbStopRecord(IntPtr self, IntPtr sel, IntPtr sender)
        => Dispatcher.UIThread.Post(() => _instance?.RecordStopRequested?.Invoke());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CbSettings(IntPtr self, IntPtr sel, IntPtr sender)
        => Dispatcher.UIThread.Post(() => _instance?.SettingsRequested?.Invoke());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CbHistory(IntPtr self, IntPtr sel, IntPtr sender)
        => Dispatcher.UIThread.Post(() => _instance?.HistoryRequested?.Invoke());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CbUpdate(IntPtr self, IntPtr sel, IntPtr sender)
        => Dispatcher.UIThread.Post(() => _instance?.UpdateRequested?.Invoke());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CbQuit(IntPtr self, IntPtr sel, IntPtr sender)
        => Dispatcher.UIThread.Post(() => _instance?.ExitRequested?.Invoke());

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetStatus(string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_statusMenuItem != IntPtr.Zero)
                SendVoidP(_statusMenuItem, Sel("setTitle:"), NSString(status));

            IntPtr btn = Send(_statusItem, Sel("button"));
            if (btn != IntPtr.Zero)
                SendVoidP(btn, Sel("setToolTip:"), NSString($"Transkript — {status}"));
        });
    }

    public void ShowUpdateAvailable(string version)
    {
        Dispatcher.UIThread.Post(() =>
        {
            string title = $"Mise à jour {version} disponible";

            if (_updateMenuItem != IntPtr.Zero)
            {
                SendVoidP(_updateMenuItem, Sel("setTitle:"), NSString(title));
                return;
            }

            IntPtr targetCls = GetClass("TranskriptMenuTarget");
            IntPtr target    = Send(Send(targetCls, Sel("alloc")), Sel("init"));

            // Insert before last separator+Quitter (2 items from end)
            nint count = (nint)Send(_menu, Sel("numberOfItems"));
            nint index = count - 2;

            _updateMenuItem = SendPPP(
                Send(GetClass("NSMenuItem"), Sel("alloc")),
                Sel("initWithTitle:action:keyEquivalent:"),
                NSString(title), Sel("onUpdate:"), NSString(""));

            SendVoidP(_updateMenuItem, Sel("setTarget:"), target);
            SendVoidB(_updateMenuItem, Sel("setEnabled:"), true);
            SendVoidPI(_menu, Sel("insertItem:atIndex:"), _updateMenuItem, index);
        });
    }

    // ── NSString helper ───────────────────────────────────────────────────────

    private static IntPtr NSString(string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s + "\0");
        fixed (byte* p = bytes)
            return SendP(GetClass("NSString"),
                         Sel("stringWithUTF8String:"), (IntPtr)p);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_instance == this) _instance = null;
        try
        {
            IntPtr statusBar = Send(GetClass("NSStatusBar"), Sel("systemStatusBar"));
            SendVoidP(statusBar, Sel("removeStatusItem:"), _statusItem);
        }
        catch { }
    }
}
