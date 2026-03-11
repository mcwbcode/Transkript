using System;
using System.IO;

namespace Transkript;

/// <summary>
/// Écrit des entrées horodatées dans %APPDATA%\SuperTranscript\logs\transkript.log
/// </summary>
public static class Logger
{
    private static readonly string LogDir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SuperTranscript", "logs");

    private static readonly string LogFile = Path.Combine(LogDir, "transkript.log");

    private static readonly object _lock = new();

    public static void Init()
    {
        Directory.CreateDirectory(LogDir);

        // Garde seulement les 500 dernières lignes pour éviter un fichier énorme
        if (File.Exists(LogFile))
        {
            var lines = File.ReadAllLines(LogFile);
            if (lines.Length > 500)
                File.WriteAllLines(LogFile, lines[^500..]);
        }

        Write("═══════════════ SESSION DÉMARRÉE ═══════════════");
        Write($"OS  : {Environment.OSVersion}");
        Write($"CPU : {Environment.ProcessorCount} cœurs");
        Write($"CLR : {Environment.Version}");
    }

    public static void Write(string message)
    {
        try
        {
            lock (_lock)
            {
                string entry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                File.AppendAllText(LogFile, entry + Environment.NewLine);
            }
        }
        catch { /* ne jamais crasher à cause des logs */ }
    }

    public static string GetLogPath() => LogFile;
}
