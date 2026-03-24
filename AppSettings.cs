using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Transkript;

public class DictionaryEntry
{
    public string From { get; set; } = "";
    public string To   { get; set; } = "";
}

public class AppSettings
{
    // ── Hotkey ──────────────────────────────────────────────────────────────
    public int    HotkeyVk   { get; set; } = NativeMethods.VK_RCONTROL;
    public string HotkeyName { get; set; } = "Ctrl Droit";

    // ── Language ────────────────────────────────────────────────────────────
    /// <summary>ISO 639-1 code ("fr", "en", "es", "de", "it", "pt", "nl") or "auto".</summary>
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
    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "SuperTranscript", "settings.json");

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
