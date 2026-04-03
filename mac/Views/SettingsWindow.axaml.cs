using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Transkript.Platform;

namespace Transkript.Views;

public partial class SettingsWindow : Window
{
    // ── Carbon VK lookup (Avalonia Key → macOS virtual key code) ─────────────
    private static readonly Dictionary<Key, uint> KeyToVK = new()
    {
        // Letters
        { Key.A, 0 },  { Key.S, 1 },  { Key.D, 2 },  { Key.F, 3 },
        { Key.H, 4 },  { Key.G, 5 },  { Key.Z, 6 },  { Key.X, 7 },
        { Key.C, 8 },  { Key.V, 9 },  { Key.B, 11 }, { Key.Q, 12 },
        { Key.W, 13 }, { Key.E, 14 }, { Key.R, 15 }, { Key.Y, 16 },
        { Key.T, 17 }, { Key.O, 31 }, { Key.U, 32 }, { Key.I, 34 },
        { Key.P, 35 }, { Key.L, 37 }, { Key.J, 38 }, { Key.K, 40 },
        { Key.N, 45 }, { Key.M, 46 },
        // Numbers (main row)
        { Key.D1, 18 }, { Key.D2, 19 }, { Key.D3, 20 }, { Key.D4, 21 },
        { Key.D5, 23 }, { Key.D6, 22 }, { Key.D7, 26 }, { Key.D8, 28 },
        { Key.D9, 25 }, { Key.D0, 29 },
        // Function keys
        { Key.F1,  122 }, { Key.F2,  120 }, { Key.F3,  99  }, { Key.F4,  118 },
        { Key.F5,  96  }, { Key.F6,  97  }, { Key.F7,  98  }, { Key.F8,  100 },
        { Key.F9,  101 }, { Key.F10, 109 }, { Key.F11, 103 }, { Key.F12, 111 },
        { Key.F13, 105 }, { Key.F14, 107 }, { Key.F15, 113 }, { Key.F16, 106 },
        { Key.F17, 64  }, { Key.F18, 79  }, { Key.F19, 80  },
        // Special keys
        { Key.Tab,    48 }, { Key.Space,  49 }, { Key.Return, 36 },
        { Key.Escape, 53 }, { Key.Back,   51 }, { Key.Delete, 117 },
        // Navigation
        { Key.Left, 123 }, { Key.Right, 124 }, { Key.Down, 125 }, { Key.Up, 126 },
        { Key.Home, 115 }, { Key.End,  119  },
        { Key.PageUp, 116 }, { Key.PageDown, 121 },
    };

    // Modifier-only keys — skip these during capture
    private static readonly HashSet<Key> ModifierKeys =
    [
        Key.LeftCtrl, Key.RightCtrl,
        Key.LeftAlt,  Key.RightAlt,
        Key.LeftShift, Key.RightShift,
        Key.LWin, Key.RWin,
        Key.System, Key.None,
    ];

    // ── Exposed results ───────────────────────────────────────────────────────
    public uint   NewHotkeyCode        { get; private set; }
    public uint   NewHotkeyModifiers   { get; private set; }
    public string NewHotkeyName        { get; private set; } = "F13";
    public string NewLanguage          { get; private set; } = "fr";
    public bool   NewRemoveFillers     { get; private set; }
    public bool   NewAutoCapitalize    { get; private set; }
    public bool   NewRemoveDuplicates  { get; private set; }
    public bool   NewSaveHistory       { get; private set; }
    public List<DictionaryEntry> NewPersonalDictionary { get; private set; } = new();

    private readonly List<DictionaryEntry> _dictEntries = new();

    // ── Key recorder state ────────────────────────────────────────────────────
    private bool   _isRecording  = false;
    private uint   _capturedCode;
    private uint   _capturedMods;
    private string _capturedName = "";

    private const string AccountUrl = "https://transkript.app/account";
    private const string BillingUrl = "https://transkript.app/billing";

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        PointerPressed += (_, e) => BeginMoveDrag(e);

        // Init recorder state from current settings
        _capturedCode = settings.HotkeyCode;
        _capturedMods = settings.HotkeyModifiers;
        _capturedName = settings.HotkeyName;
        UpdateHotkeyDisplay(idle: true);

        // Language ComboBox
        SelectLanguage(settings.Language);

        // Text options
        ChkFillers.IsChecked    = settings.RemoveFillers;
        ChkDuplicates.IsChecked = settings.RemoveDuplicates;
        ChkCapitalize.IsChecked = settings.AutoCapitalize;
        ChkHistory.IsChecked    = settings.SaveHistory;

        // Dictionary
        foreach (var entry in settings.PersonalDictionary)
        {
            _dictEntries.Add(new DictionaryEntry { From = entry.From, To = entry.To });
            AddDictRow(entry.From, entry.To);
        }

        LoadAccountInfo();
    }

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
                AccountPlanBadge.Background      = new SolidColorBrush(Color.Parse("#F0FDF4"));
                AccountPlanBadge.BorderBrush     = new SolidColorBrush(Color.Parse("#BBF7D0"));
                AccountPlanBadge.BorderThickness = new Avalonia.Thickness(1);
                TxtAccountPlan.Foreground        = new SolidColorBrush(Color.Parse("#16A34A"));
                break;
            case "beta":
                TxtAccountPlan.Text              = "BETA";
                AccountPlanBadge.Background      = new SolidColorBrush(Color.Parse("#FFFBEB"));
                AccountPlanBadge.BorderBrush     = new SolidColorBrush(Color.Parse("#FDE68A"));
                AccountPlanBadge.BorderThickness = new Avalonia.Thickness(1);
                TxtAccountPlan.Foreground        = new SolidColorBrush(Color.Parse("#D97706"));
                break;
            default:
                TxtAccountPlan.Text              = "FREE";
                AccountPlanBadge.Background      = new SolidColorBrush(Color.Parse("#F5F5F5"));
                AccountPlanBadge.BorderBrush     = new SolidColorBrush(Color.Parse("#EBEBEB"));
                AccountPlanBadge.BorderThickness = new Avalonia.Thickness(1);
                TxtAccountPlan.Foreground        = new SolidColorBrush(Color.Parse("#6B6B6B"));
                break;
        }
    }

    // ── Key recorder ──────────────────────────────────────────────────────────

    private void HotkeyRecorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isRecording) { StopRecording(cancelled: true); return; }
        StartRecording();
    }

    private void StartRecording()
    {
        _isRecording = true;
        UpdateHotkeyDisplay(idle: false);
        // Tunnel phase = before focus/tab traversal handles the key
        this.AddHandler(KeyDownEvent, OnKeyCapture, RoutingStrategies.Tunnel);
    }

    private void StopRecording(bool cancelled = false)
    {
        _isRecording = false;
        this.RemoveHandler(KeyDownEvent, OnKeyCapture);
        UpdateHotkeyDisplay(idle: true);
        if (cancelled) Logger.Write("KeyRecorder: annulé");
    }

    private void OnKeyCapture(object? sender, KeyEventArgs e)
    {
        e.Handled = true; // prevent Tab from changing focus, etc.

        // Ignore pure modifier presses
        if (ModifierKeys.Contains(e.Key)) return;

        // Escape = cancel without saving
        if (e.Key == Key.Escape)
        {
            StopRecording(cancelled: true);
            return;
        }

        if (!KeyToVK.TryGetValue(e.Key, out uint vk))
        {
            // Key not in our map — show a hint but stay in recording mode
            TxtHotkeyKey.Text = $"{e.Key} (non supporté)";
            return;
        }

        // Build Carbon modifier mask
        uint mods = 0;
        var km = e.KeyModifiers;
        if ((km & KeyModifiers.Control) != 0) mods |= GlobalHotkeyMac.controlKey;
        if ((km & KeyModifiers.Alt)     != 0) mods |= GlobalHotkeyMac.optionKey;
        if ((km & KeyModifiers.Shift)   != 0) mods |= GlobalHotkeyMac.shiftKey;
        if ((km & KeyModifiers.Meta)    != 0) mods |= GlobalHotkeyMac.cmdKey;

        _capturedCode = vk;
        _capturedMods = mods;
        _capturedName = FormatKeyName(e.Key, km);

        Logger.Write($"KeyRecorder: capturé {_capturedName} (vk={vk}, mods={mods})");
        StopRecording();
    }

    private void UpdateHotkeyDisplay(bool idle)
    {
        if (idle)
        {
            HotkeyRecorderBorder.BorderBrush = new SolidColorBrush(Color.Parse("#EBEBEB"));
            HotkeyRecorderBorder.Background  = new SolidColorBrush(Color.Parse("#F7F7F7"));
            TxtHotkeyKey.Text      = _capturedName;
            TxtHotkeyKey.Foreground = new SolidColorBrush(Color.Parse("#0D0D0D"));
            TxtHotkeyHint.Text      = "Cliquer pour changer";
            TxtHotkeyHint.Foreground = new SolidColorBrush(Color.Parse("#C0C0C0"));
        }
        else
        {
            HotkeyRecorderBorder.BorderBrush = new SolidColorBrush(Color.Parse("#0D0D0D"));
            HotkeyRecorderBorder.Background  = new SolidColorBrush(Color.Parse("#F0F0F0"));
            TxtHotkeyKey.Text      = "En attente…";
            TxtHotkeyKey.Foreground = new SolidColorBrush(Color.Parse("#6B6B6B"));
            TxtHotkeyHint.Text      = "Appuyez sur une touche  ·  Échap pour annuler";
            TxtHotkeyHint.Foreground = new SolidColorBrush(Color.Parse("#B0B0B0"));
        }
    }

    private static string FormatKeyName(Key key, KeyModifiers mods)
    {
        var parts = new List<string>();
        if ((mods & KeyModifiers.Control) != 0) parts.Add("⌃");
        if ((mods & KeyModifiers.Alt)     != 0) parts.Add("⌥");
        if ((mods & KeyModifiers.Shift)   != 0) parts.Add("⇧");
        if ((mods & KeyModifiers.Meta)    != 0) parts.Add("⌘");

        string keyStr = key switch
        {
            Key.Space    => "Espace",
            Key.Return   => "Entrée",
            Key.Tab      => "Tab",
            Key.Escape   => "Échap",
            Key.Back     => "Suppr",
            Key.Delete   => "Suppr →",
            Key.Left     => "←",
            Key.Right    => "→",
            Key.Up       => "↑",
            Key.Down     => "↓",
            Key.Home     => "Début",
            Key.End      => "Fin",
            Key.PageUp   => "Page↑",
            Key.PageDown => "Page↓",
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => key.ToString()[1..], // "D1" → "1"
            >= Key.F1 and <= Key.F19 => key.ToString(),
            _ => key.ToString(),
        };

        parts.Add(keyStr);
        return string.Join("", parts);
    }

    // ── Language helpers ──────────────────────────────────────────────────────

    private void SelectLanguage(string code)
    {
        foreach (var item in CmbLanguage.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == code)
            {
                CmbLanguage.SelectedItem = cbi;
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

    // ── Dictionary ────────────────────────────────────────────────────────────

    private void BtnAddDict_Click(object? sender, RoutedEventArgs e)
    {
        string from = TxtDictFrom.Text?.Trim() ?? "";
        string to   = TxtDictTo.Text?.Trim()   ?? "";
        if (string.IsNullOrEmpty(from)) return;

        _dictEntries.Add(new DictionaryEntry { From = from, To = to });
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
            BorderBrush     = new SolidColorBrush(Color.Parse("#EBEBEB")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius    = new Avalonia.CornerRadius(8),
            Padding         = new Avalonia.Thickness(12, 7),
            Margin          = new Avalonia.Thickness(0, 0, 0, 4)
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        string displayTo = string.IsNullOrEmpty(to) ? "(supprimé)" : to;

        sp.Children.Add(new TextBlock { Text = from, Foreground = new SolidColorBrush(Color.Parse("#0D0D0D")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 6, 0) });
        sp.Children.Add(new TextBlock { Text = "→",  Foreground = new SolidColorBrush(Color.Parse("#C0C0C0")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 6, 0) });
        sp.Children.Add(new TextBlock { Text = displayTo, Foreground = new SolidColorBrush(string.IsNullOrEmpty(to) ? Color.Parse("#C0C0C0") : Color.Parse("#16A34A")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

        var btnDel = new Button
        {
            Content = "✕", Margin = new Avalonia.Thickness(8, 0, 0, 0),
            Padding = new Avalonia.Thickness(0), Width = 20, Height = 20, FontSize = 10,
            Background = new SolidColorBrush(Color.Parse("#F0F0F0")),
            Foreground = new SolidColorBrush(Color.Parse("#6B6B6B")),
            BorderThickness = new Avalonia.Thickness(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        btnDel.Click += (_, _) => { DictPanel.Children.Remove(row); RebuildDictEntries(); };

        sp.Children.Add(btnDel);
        row.Child = sp;
        DictPanel.Children.Add(row);
    }

    private void RebuildDictEntries()
    {
        _dictEntries.Clear();
        foreach (var child in DictPanel.Children)
        {
            if (child is Border b && b.Child is StackPanel sp && sp.Children.Count >= 3)
            {
                string f = ((TextBlock)sp.Children[0]).Text ?? "";
                string t = ((TextBlock)sp.Children[2]).Text ?? "";
                if (t == "(supprimé)") t = "";
                _dictEntries.Add(new DictionaryEntry { From = f, To = t });
            }
        }
    }

    // ── Buttons ───────────────────────────────────────────────────────────────

    private void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        NewHotkeyCode       = _capturedCode;
        NewHotkeyModifiers  = _capturedMods;
        NewHotkeyName       = _capturedName;
        NewLanguage         = GetSelectedLanguage();
        NewRemoveFillers    = ChkFillers.IsChecked    == true;
        NewAutoCapitalize   = ChkCapitalize.IsChecked == true;
        NewRemoveDuplicates = ChkDuplicates.IsChecked == true;
        NewSaveHistory      = ChkHistory.IsChecked    == true;
        NewPersonalDictionary = new List<DictionaryEntry>(_dictEntries);

        Tag = true;
        Close();
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        if (_isRecording) { StopRecording(cancelled: true); return; }
        Tag = false;
        Close();
    }

    private void BtnManageAccount_Click(object? sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(AccountUrl) { UseShellExecute = true });

    private void BtnManageBilling_Click(object? sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(BillingUrl) { UseShellExecute = true });
}
