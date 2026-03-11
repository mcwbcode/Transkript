using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfColor     = System.Windows.Media.Color;

namespace Transkript;

public partial class SettingsWindow : Window
{
    private bool _listening = false;

    // Result exposed after DialogResult = true
    public int    NewHotkeyVk   { get; private set; }
    public string NewHotkeyName { get; private set; } = "";

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        NewHotkeyVk   = settings.HotkeyVk;
        NewHotkeyName = settings.HotkeyName;
        TxtKey.Text   = settings.HotkeyName;
    }

    // ── Key capture ──────────────────────────────────────────────────────────

    private void KeyBorder_Click(object sender, MouseButtonEventArgs e)
    {
        StartListening();
    }

    private void StartListening()
    {
        _listening           = true;
        KeyBorder.Visibility = Visibility.Collapsed;
        TxtListening.Visibility = Visibility.Visible;
        KeyBorder.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x9F, 0x0A));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_listening) { base.OnPreviewKeyDown(e); return; }

        e.Handled = true;

        // Escape cancels listening
        if (e.Key == Key.Escape)
        {
            StopListening();
            return;
        }

        // Ignore lone modifier keys (use them only as part of a combo)
        // Here we accept any key (including modifiers), so the user can bind e.g. Ctrl Droit alone
        int vk = KeyInterop.VirtualKeyFromKey(e.Key);
        if (vk == 0) { StopListening(); return; }

        NewHotkeyVk   = vk;
        NewHotkeyName = FriendlyName(e.Key);
        TxtKey.Text   = NewHotkeyName;

        StopListening();
    }

    private void StopListening()
    {
        _listening              = false;
        TxtListening.Visibility = Visibility.Collapsed;
        KeyBorder.Visibility    = Visibility.Visible;
        KeyBorder.BorderBrush   = new SolidColorBrush(WpfColor.FromRgb(0x3A, 0x3A, 0x3C));
    }

    // ── Buttons ──────────────────────────────────────────────────────────────

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    // ── Key name helpers ─────────────────────────────────────────────────────

    private static readonly Dictionary<Key, string> _names = new()
    {
        { Key.RightCtrl,  "Ctrl Droit"   },
        { Key.LeftCtrl,   "Ctrl Gauche"  },
        { Key.RightShift, "Shift Droit"  },
        { Key.LeftShift,  "Shift Gauche" },
        { Key.RightAlt,   "Alt Droit"    },
        { Key.LeftAlt,    "Alt Gauche"   },
        { Key.LWin,       "Win Gauche"   },
        { Key.RWin,       "Win Droit"    },
        { Key.CapsLock,   "Verr Maj"     },
        { Key.Tab,        "Tab"          },
        { Key.Space,      "Espace"       },
        { Key.F1,  "F1"  }, { Key.F2,  "F2"  }, { Key.F3,  "F3"  },
        { Key.F4,  "F4"  }, { Key.F5,  "F5"  }, { Key.F6,  "F6"  },
        { Key.F7,  "F7"  }, { Key.F8,  "F8"  }, { Key.F9,  "F9"  },
        { Key.F10, "F10" }, { Key.F11, "F11" }, { Key.F12, "F12" },
    };

    private static string FriendlyName(Key key)
        => _names.TryGetValue(key, out string? name) ? name : key.ToString();
}
