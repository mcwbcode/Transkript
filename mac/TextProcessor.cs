using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Transkript;

public static class TextProcessor
{
    private static readonly string[] FrenchFillers =
    [
        "tu vois", "tu sais", "c'est-à-dire", "en fait",
        "genre", "quoi", "voilà", "donc", "alors",
        "euh", "heu", "hem", "hum", "hm",
        "ben", "bah", "eh", "ah", "oh"
    ];

    private static readonly string[] EnglishFillers =
    [
        "you know", "i mean", "you see", "sort of", "kind of",
        "basically", "literally", "honestly", "actually",
        "um", "uh", "hmm", "hm", "er", "ah"
    ];

    private static readonly string[] UniversalFillers = ["euh", "um", "uh", "hmm", "hm"];

    public static string Process(string raw, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        string text = raw;

        if (settings.RemoveFillers)
            text = RemoveFillers(text, settings.Language);

        if (settings.RemoveDuplicates)
            text = RemoveDuplicateWords(text);

        if (settings.PersonalDictionary.Count > 0)
            text = ApplyDictionary(text, settings.PersonalDictionary);

        if (settings.AutoCapitalize)
            text = AutoCapitalize(text);

        return text.Trim();
    }

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
            var pattern = @"(?<![a-zA-ZÀ-ÿ])" + Regex.Escape(filler) + @"(?![a-zA-ZÀ-ÿ])[,.]?";
            text = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase);
        }

        text = Regex.Replace(text, @"  +", " ");
        text = Regex.Replace(text, @"\s+([,\.!?;:])", "$1");
        return text.Trim();
    }

    public static string RemoveDuplicateWords(string text)
    {
        return Regex.Replace(text,
            @"\b(\w+)(?:\s+\1)+\b",
            "$1",
            RegexOptions.IgnoreCase);
    }

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

    public static string AutoCapitalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return char.ToUpper(text[0]) + text[1..];
    }

    public static int CountWords(string text)
        => string.IsNullOrWhiteSpace(text)
            ? 0
            : Regex.Matches(text.Trim(), @"\b\w+\b").Count;
}
