using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Transkript;

public class HistoryEntry
{
    public DateTime Timestamp { get; set; }
    public string   Text      { get; set; } = "";
    public int      WordCount { get; set; }
    public string   Language  { get; set; } = "fr";
}

public static class HistoryManager
{
    private static readonly string HistoryDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "Transkript");

    private static readonly string HistoryPath = Path.Combine(HistoryDir, "history.jsonl");

    public static void Append(string text, string language)
    {
        try
        {
            Directory.CreateDirectory(HistoryDir);
            var entry = new HistoryEntry
            {
                Timestamp = DateTime.Now,
                Text      = text,
                WordCount = TextProcessor.CountWords(text),
                Language  = language
            };
            File.AppendAllText(HistoryPath, JsonSerializer.Serialize(entry) + "\n");
        }
        catch (Exception ex)
        {
            Logger.Write($"HistoryManager.Append : erreur — {ex.Message}");
        }
    }

    public static List<HistoryEntry> LoadAll()
    {
        var entries = new List<HistoryEntry>();
        if (!File.Exists(HistoryPath)) return entries;

        try
        {
            foreach (var line in File.ReadLines(HistoryPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = JsonSerializer.Deserialize<HistoryEntry>(line);
                if (entry != null) entries.Add(entry);
            }
        }
        catch (Exception ex)
        {
            Logger.Write($"HistoryManager.LoadAll : erreur — {ex.Message}");
        }

        return entries;
    }

    public static int GetTodayWordCount()
    {
        try
        {
            var today = DateTime.Today;
            return LoadAll().Where(e => e.Timestamp.Date == today).Sum(e => e.WordCount);
        }
        catch { return 0; }
    }

    public static int GetTotalWordCount()
    {
        try { return LoadAll().Sum(e => e.WordCount); }
        catch { return 0; }
    }

    public static void OpenHistoryFolder()
    {
        try
        {
            Directory.CreateDirectory(HistoryDir);
            Process.Start("open", HistoryDir);
        }
        catch { }
    }
}
