using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Transkript.Platform;

namespace Transkript;

public class DictionaryEntry
{
    public string From { get; set; } = "";
    public string To   { get; set; } = "";
}

public class AppSettings
{
    // ── Hotkey (macOS Carbon key codes) ─────────────────────────────────────
    public uint   HotkeyCode      { get; set; } = GlobalHotkeyMac.VK_F13;
    public uint   HotkeyModifiers { get; set; } = 0;
    public string HotkeyName      { get; set; } = "F13";

    // ── Language ────────────────────────────────────────────────────────────
    public string Language { get; set; } = "fr";

    // ── Text processing ─────────────────────────────────────────────────────
    public bool RemoveFillers    { get; set; } = true;
    public bool AutoCapitalize   { get; set; } = true;
    public bool RemoveDuplicates { get; set; } = true;

    // ── Personal dictionary ─────────────────────────────────────────────────
    public List<DictionaryEntry> PersonalDictionary { get; set; } = new();

    // ── History ─────────────────────────────────────────────────────────────
    public bool SaveHistory { get; set; } = true;

    // ── Paths ────────────────────────────────────────────────────────────────
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "Transkript", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, opts));
        }
        catch { }
    }
}
