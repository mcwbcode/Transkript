using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Transkript;

/// <summary>
/// Post-processes raw Whisper transcription output:
///   • Filler word removal  (euh, hum, ben, etc.)
///   • Personal dictionary  (user-defined substitutions)
///   • Duplicate word removal  (repeated word stutters)
///   • Auto-capitalize first letter
/// </summary>
public static class TextProcessor
{
    // ── French filler words ───────────────────────────────────────────────
    // Ordered longest → shortest to avoid partial replacements
    private static readonly string[] FrenchFillers =
    [
        "tu vois", "tu sais", "c'est-à-dire", "en fait",
        "genre", "quoi", "voilà", "donc", "alors",
        "euh", "heu", "hem", "hum", "hm",
        "ben", "bah", "eh", "ah", "oh"
    ];

    // ── English filler words ──────────────────────────────────────────────
    private static readonly string[] EnglishFillers =
    [
        "you know", "i mean", "you see", "sort of", "kind of",
        "basically", "literally", "honestly", "actually",
        "um", "uh", "hmm", "hm", "er", "ah"
    ];

    // ── Multi-language fillers (always stripped) ──────────────────────────
    private static readonly string[] UniversalFillers = ["euh", "um", "uh", "hmm", "hm"];

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Applies all enabled processing steps and returns the cleaned text.</summary>
    public static string Process(string raw, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        string text = raw;

        // 1. Remove filler words
        if (settings.RemoveFillers)
            text = RemoveFillers(text, settings.Language);

        // 2. Remove duplicate consecutive words (e.g. "le le chat" → "le chat")
        if (settings.RemoveDuplicates)
            text = RemoveDuplicateWords(text);

        // 3. Apply personal dictionary
        if (settings.PersonalDictionary.Count > 0)
            text = ApplyDictionary(text, settings.PersonalDictionary);

        // 4. Auto-capitalize first letter
        if (settings.AutoCapitalize)
            text = AutoCapitalize(text);

        return text.Trim();
    }

    // ── Filler removal ────────────────────────────────────────────────────

    public static string RemoveFillers(string text, string language)
    {
        var fillers = language switch
        {
            "fr" => FrenchFillers,
            "en" => EnglishFillers,
            _    => UniversalFillers
        };

        foreach (var filler in fillers)
        {
            // Match whole word/phrase, case-insensitive, with optional surrounding punctuation
            var pattern = @"(?<![a-zA-ZÀ-ÿ])" + Regex.Escape(filler) + @"(?![a-zA-ZÀ-ÿ])[,.]?";
            text = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase);
        }

        // Collapse multiple spaces into one, fix ". ," artifacts
        text = Regex.Replace(text, @"  +", " ");
        text = Regex.Replace(text, @"\s+([,\.!?;:])", "$1");
        return text.Trim();
    }

    // ── Duplicate word removal ────────────────────────────────────────────

    public static string RemoveDuplicateWords(string text)
    {
        // Catches stutters like "je je veux" or "le le le chat"
        return Regex.Replace(text,
            @"\b(\w+)(?:\s+\1)+\b",
            "$1",
            RegexOptions.IgnoreCase);
    }

    // ── Personal dictionary ───────────────────────────────────────────────

    public static string ApplyDictionary(string text, List<DictionaryEntry> dict)
    {
        foreach (var entry in dict)
        {
            if (string.IsNullOrWhiteSpace(entry.From)) continue;
            var pattern = @"(?<![a-zA-ZÀ-ÿ])" + Regex.Escape(entry.From) + @"(?![a-zA-ZÀ-ÿ])";
            text = Regex.Replace(text, pattern, entry.To, RegexOptions.IgnoreCase);
        }
        return text;
    }

    // ── Auto-capitalize ───────────────────────────────────────────────────

    public static string AutoCapitalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return char.ToUpper(text[0]) + text[1..];
    }

    // ── Word count ────────────────────────────────────────────────────────

    public static int CountWords(string text)
        => string.IsNullOrWhiteSpace(text)
            ? 0
            : Regex.Matches(text.Trim(), @"\b\w+\b").Count;
}
