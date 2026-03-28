using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Transkript.Platform;

namespace Transkript.Views;

public partial class SettingsWindow : Window
{
    // ── Hotkey options (macOS Carbon key codes) ───────────────────────────────
    private static readonly (string Name, uint Code, uint Mods)[] HotkeyOptions =
    [
        ("F13",         GlobalHotkeyMac.VK_F13,       0),
        ("F14",         GlobalHotkeyMac.VK_F14,       0),
        ("F15",         GlobalHotkeyMac.VK_F15,       0),
        ("F16",         GlobalHotkeyMac.VK_F16,       0),
        ("F17",         GlobalHotkeyMac.VK_F17,       0),
        ("F18",         GlobalHotkeyMac.VK_F18,       0),
        ("F19",         GlobalHotkeyMac.VK_F19,       0),
        ("⌘ Droit",     GlobalHotkeyMac.VK_RIGHT_CMD, 0),
        ("CapsLock",    GlobalHotkeyMac.VK_CAPS_LOCK, 0),
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

    private const string AccountUrl = "https://transkript.app/account";
    private const string BillingUrl = "https://transkript.app/billing";

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        PointerPressed += (_, e) => BeginMoveDrag(e);

        // Hotkey ComboBox
        foreach (var (name, _, _) in HotkeyOptions)
            CmbHotkey.Items.Add(name);
        SelectHotkey(settings.HotkeyCode);

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

        // Account
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

    // ── Hotkey helpers ────────────────────────────────────────────────────────

    private void SelectHotkey(uint code)
    {
        for (int i = 0; i < HotkeyOptions.Length; i++)
        {
            if (HotkeyOptions[i].Code == code)
            {
                CmbHotkey.SelectedIndex = i;
                return;
            }
        }
        CmbHotkey.SelectedIndex = 0; // default F13
    }

    private (uint code, uint mods, string name) GetSelectedHotkey()
    {
        int idx = CmbHotkey.SelectedIndex;
        if (idx < 0 || idx >= HotkeyOptions.Length) idx = 0;
        var (name, code, mods) = HotkeyOptions[idx];
        return (code, mods, name);
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

        sp.Children.Add(new TextBlock
        {
            Text              = from,
            Foreground        = new SolidColorBrush(Color.Parse("#0D0D0D")),
            FontSize          = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Avalonia.Thickness(0, 0, 6, 0)
        });
        sp.Children.Add(new TextBlock
        {
            Text              = "→",
            Foreground        = new SolidColorBrush(Color.Parse("#C0C0C0")),
            FontSize          = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Avalonia.Thickness(0, 0, 6, 0)
        });
        sp.Children.Add(new TextBlock
        {
            Text              = displayTo,
            Foreground        = new SolidColorBrush(string.IsNullOrEmpty(to)
                ? Color.Parse("#C0C0C0")
                : Color.Parse("#16A34A")),
            FontSize          = 12,
            VerticalAlignment = VerticalAlignment.Center
        });

        var btnDel = new Button
        {
            Content         = "✕",
            Margin          = new Avalonia.Thickness(8, 0, 0, 0),
            Padding         = new Avalonia.Thickness(0),
            Width           = 20,
            Height          = 20,
            FontSize        = 10,
            Background      = new SolidColorBrush(Color.Parse("#F0F0F0")),
            Foreground      = new SolidColorBrush(Color.Parse("#6B6B6B")),
            BorderThickness = new Avalonia.Thickness(0),
            Cursor          = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        btnDel.Click += (_, _) =>
        {
            DictPanel.Children.Remove(row);
            RebuildDictEntries();
        };

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
        var (code, mods, name) = GetSelectedHotkey();
        NewHotkeyCode       = code;
        NewHotkeyModifiers  = mods;
        NewHotkeyName       = name;
        NewLanguage         = GetSelectedLanguage();
        NewRemoveFillers    = ChkFillers.IsChecked    == true;
        NewAutoCapitalize   = ChkCapitalize.IsChecked  == true;
        NewRemoveDuplicates = ChkDuplicates.IsChecked  == true;
        NewSaveHistory      = ChkHistory.IsChecked     == true;
        NewPersonalDictionary = new List<DictionaryEntry>(_dictEntries);

        Tag = true;
        Close();
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Tag = false;
        Close();
    }

    private void BtnManageAccount_Click(object? sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(AccountUrl) { UseShellExecute = true });

    private void BtnManageBilling_Click(object? sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(BillingUrl) { UseShellExecute = true });
}
