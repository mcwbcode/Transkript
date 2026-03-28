using System;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;

namespace Transkript.Platform;

/// <summary>
/// Creates and manages a macOS menu bar (NSStatusBar) icon with a context menu.
/// Mirrors the Windows TrayManager behaviour.
///
/// Note: Avalonia 11 has built-in TrayIcon support. We use it here via Avalonia's
/// managed API rather than raw AppKit P/Invoke for reliability.
/// The native NSStatusItem is used only for the status title in the menu bar.
/// </summary>
public sealed class MenuBarMac : IDisposable
{
    public event Action? ExitRequested;
    public event Action? SettingsRequested;
    public event Action? HistoryRequested;
    public event Action? UpdateRequested;

    private readonly Avalonia.Controls.TrayIcon _trayIcon;
    private Avalonia.Controls.NativeMenuItem?   _statusItem;
    private Avalonia.Controls.NativeMenuItem?   _updateItem;
    private bool _disposed;

    public MenuBarMac(string appName)
    {
        var menu = new Avalonia.Controls.NativeMenu();

        // Header label (app name, disabled)
        var header = new Avalonia.Controls.NativeMenuItem(appName) { IsEnabled = false };
        menu.Add(header);
        menu.Add(new Avalonia.Controls.NativeMenuItemSeparator());

        // Status line (shows "Prêt — F13 pour dicter", updated dynamically)
        _statusItem = new Avalonia.Controls.NativeMenuItem("Chargement…") { IsEnabled = false };
        menu.Add(_statusItem);
        menu.Add(new Avalonia.Controls.NativeMenuItemSeparator());

        // Actions
        var settings = new Avalonia.Controls.NativeMenuItem("Paramètres…");
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        menu.Add(settings);

        var history = new Avalonia.Controls.NativeMenuItem("Historique");
        history.Click += (_, _) => HistoryRequested?.Invoke();
        menu.Add(history);

        menu.Add(new Avalonia.Controls.NativeMenuItemSeparator());

        var quit = new Avalonia.Controls.NativeMenuItem("Quitter");
        quit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Add(quit);

        // Tray icon
        _trayIcon = new Avalonia.Controls.TrayIcon
        {
            ToolTipText = appName,
            Menu        = menu,
            // Use a simple text icon; replace with icns at build time
            Icon        = LoadIcon(),
        };

        // Show the tray icon
        Avalonia.Controls.TrayIcon.SetIcons(
            Avalonia.Application.Current!,
            new Avalonia.Controls.TrayIcons { _trayIcon });
    }

    private static Avalonia.Media.Imaging.Bitmap? LoadIcon()
    {
        try
        {
            string iconPath = System.IO.Path.Combine(
                AppContext.BaseDirectory, "Assets", "Transkript.icns");
            if (System.IO.File.Exists(iconPath))
                return new Avalonia.Media.Imaging.Bitmap(iconPath);
        }
        catch { }
        return null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetStatus(string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_statusItem != null)
                _statusItem.Header = status;
            _trayIcon.ToolTipText = $"Transkript — {status}";
        });
    }

    public void ShowUpdateAvailable(string version)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_updateItem == null)
            {
                _updateItem = new Avalonia.Controls.NativeMenuItem($"Mise à jour {version} disponible");
                _updateItem.Click += (_, _) => UpdateRequested?.Invoke();
                // Insert before "Quitter" (last item after separator)
                var menu = _trayIcon.Menu!;
                menu.Insert(menu.Count - 2, _updateItem);
            }
            else
            {
                _updateItem.Header = $"Mise à jour {version} disponible";
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _trayIcon.Dispose();
    }
}
