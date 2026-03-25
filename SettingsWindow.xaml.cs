using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfColor     = System.Windows.Media.Color;
using Orientation  = System.Windows.Controls.Orientation;
using Button       = System.Windows.Controls.Button;
using Cursors      = System.Windows.Input.Cursors;

namespace Transkript;

public partial class SettingsWindow : Window
{
    private bool _listening = false;

    // ── Exposed results after DialogResult = true ─────────────────────────
    public int    NewHotkeyVk         { get; private set; }
    public string NewHotkeyName       { get; private set; } = "";
    public string NewLanguage         { get; private set; } = "fr";
    public bool   NewRemoveFillers    { get; private set; }
    public bool   NewAutoCapitalize   { get; private set; }
    public bool   NewRemoveDuplicates { get; private set; }
    public bool   NewSaveHistory      { get; private set; }
    public List<DictionaryEntry> NewPersonalDictionary { get; private set; } = new();

    private readonly List<DictionaryEntry> _dictEntries = new();

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();

        // ── Hotkey ───────────────────────────────────────────────────────
        NewHotkeyVk   = settings.HotkeyVk;
        NewHotkeyName = settings.HotkeyName;
        TxtKey.Text   = settings.HotkeyName;

        // ── Language ─────────────────────────────────────────────────────
        SelectLanguage(settings.Language);

        // ── Text options ─────────────────────────────────────────────────
        ChkFillers.IsChecked    = settings.RemoveFillers;
        ChkDuplicates.IsChecked = settings.RemoveDuplicates;
        ChkCapitalize.IsChecked = settings.AutoCapitalize;
        ChkHistory.IsChecked    = settings.SaveHistory;

        // ── Dictionary ───────────────────────────────────────────────────
        foreach (var entry in settings.PersonalDictionary)
        {
            _dictEntries.Add(new DictionaryEntry { From = entry.From, To = entry.To });
            AddDictRow(entry.From, entry.To);
        }

        // ── Account tab ──────────────────────────────────────────────────
        LoadAccountInfo();
    }

    private const string AccountUrl  = "https://transkript.app/account";
    private const string BillingUrl  = "https://transkript.app/billing";

    private void LoadAccountInfo()
    {
        var session = AuthService.LoadSession();
        if (session == null) return;

        TxtAccountEmail.Text = session.Email;
    }

    public void SetAccountPlan(string plan)
    {
        switch (plan)
        {
            case "pro":
                TxtAccountPlan.Text              = "PRO";
                AccountPlanBadge.Background      = new SolidColorBrush(WpfColor.FromRgb(240, 253, 244));
                AccountPlanBadge.BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(187, 247, 208));
                AccountPlanBadge.BorderThickness = new Thickness(1);
                TxtAccountPlan.Foreground        = new SolidColorBrush(WpfColor.FromRgb(22, 163, 74));
                break;
            case "beta":
                TxtAccountPlan.Text              = "BETA";
                AccountPlanBadge.Background      = new SolidColorBrush(WpfColor.FromRgb(255, 251, 235));
                AccountPlanBadge.BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(253, 230, 138));
                AccountPlanBadge.BorderThickness = new Thickness(1);
                TxtAccountPlan.Foreground        = new SolidColorBrush(WpfColor.FromRgb(217, 119, 6));
                break;
            default:
                TxtAccountPlan.Text              = "FREE";
                AccountPlanBadge.Background      = new SolidColorBrush(WpfColor.FromRgb(245, 245, 245));
                AccountPlanBadge.BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(235, 235, 235));
                AccountPlanBadge.BorderThickness = new Thickness(1);
                TxtAccountPlan.Foreground        = new SolidColorBrush(WpfColor.FromRgb(107, 107, 107));
                break;
        }
    }

    private void BtnManageAccount_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(AccountUrl) { UseShellExecute = true });

    private void BtnManageBilling_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(BillingUrl) { UseShellExecute = true });

    // ── Language helper ──────────────────────────────────────────────────

    private void SelectLanguage(string code)
    {
        foreach (ComboBoxItem item in CmbLanguage.Items)
        {
            if (item.Tag?.ToString() == code)
            {
                CmbLanguage.SelectedItem = item;
                return;
            }
        }
        CmbLanguage.SelectedIndex = 1; // default fr
    }

    private string GetSelectedLanguage()
    {
        if (CmbLanguage.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? "fr";
        return "fr";
    }

    // ── Key capture ───────────────────────────────────────────────────────

    private void KeyBorder_Click(object sender, MouseButtonEventArgs e)
    {
        StartListening();
    }

    private void StartListening()
    {
        _listening              = true;
        KeyBorder.Visibility    = Visibility.Collapsed;
        TxtListening.Visibility = Visibility.Visible;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_listening) { base.OnPreviewKeyDown(e); return; }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            StopListening();
            return;
        }

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
    }

    // ── Dictionary ────────────────────────────────────────────────────────

    private void BtnAddDict_Click(object sender, RoutedEventArgs e)
    {
        string from = TxtDictFrom.Text.Trim();
        string to   = TxtDictTo.Text.Trim();
        if (string.IsNullOrEmpty(from)) return;

        var entry = new DictionaryEntry { From = from, To = to };
        _dictEntries.Add(entry);
        AddDictRow(from, to);

        TxtDictFrom.Clear();
        TxtDictTo.Clear();
        TxtDictFrom.Focus();
    }

    private void AddDictRow(string from, string to)
    {
        var row = new Border
        {
            Background      = new SolidColorBrush(Colors.White),
            BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(0xEB, 0xEB, 0xEB)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(12, 7, 12, 7),
            Margin          = new Thickness(0, 0, 0, 4)
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal };

        string displayTo = string.IsNullOrEmpty(to) ? "(supprimé)" : to;

        var lblFrom = new TextBlock
        {
            Text              = from,
            Foreground        = new SolidColorBrush(WpfColor.FromRgb(0x0D, 0x0D, 0x0D)),
            FontSize          = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 6, 0)
        };
        var arrow = new TextBlock
        {
            Text              = "→",
            Foreground        = new SolidColorBrush(WpfColor.FromRgb(0xC0, 0xC0, 0xC0)),
            FontSize          = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 6, 0)
        };
        var lblTo = new TextBlock
        {
            Text              = displayTo,
            Foreground        = new SolidColorBrush(string.IsNullOrEmpty(to)
                ? WpfColor.FromRgb(0xC0, 0xC0, 0xC0)
                : WpfColor.FromRgb(0x16, 0xA3, 0x4A)),
            FontSize          = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        var btnDel = new Button
        {
            Content         = "✕",
            Margin          = new Thickness(8, 0, 0, 0),
            Padding         = new Thickness(0),
            Width           = 20,
            Height          = 20,
            FontSize        = 10,
            Background      = new SolidColorBrush(WpfColor.FromRgb(0xF0, 0xF0, 0xF0)),
            Foreground      = new SolidColorBrush(WpfColor.FromRgb(0x6B, 0x6B, 0x6B)),
            BorderThickness = new Thickness(0),
            Cursor          = Cursors.Hand
        };

        btnDel.Click += (_, _) =>
        {
            DictPanel.Children.Remove(row);
            RebuildDictEntries();
        };

        sp.Children.Add(lblFrom);
        sp.Children.Add(arrow);
        sp.Children.Add(lblTo);
        sp.Children.Add(btnDel);

        row.Child = sp;
        DictPanel.Children.Add(row);
    }

    private void RebuildDictEntries()
    {
        _dictEntries.Clear();
        foreach (Border row in DictPanel.Children)
        {
            if (row.Child is StackPanel sp && sp.Children.Count >= 3)
            {
                string f = ((TextBlock)sp.Children[0]).Text;
                string t = ((TextBlock)sp.Children[2]).Text;
                if (t == "(supprimé)") t = "";
                _dictEntries.Add(new DictionaryEntry { From = f, To = t });
            }
        }
    }

    // ── Buttons ──────────────────────────────────────────────────────────────

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        NewLanguage         = GetSelectedLanguage();
        NewRemoveFillers    = ChkFillers.IsChecked    == true;
        NewAutoCapitalize   = ChkCapitalize.IsChecked  == true;
        NewRemoveDuplicates = ChkDuplicates.IsChecked  == true;
        NewSaveHistory      = ChkHistory.IsChecked     == true;

        // Rebuild dictionary from panel (handles any edits)
        NewPersonalDictionary = new List<DictionaryEntry>(_dictEntries);

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

    private static readonly System.Collections.Generic.Dictionary<Key, string> _names = new()
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
