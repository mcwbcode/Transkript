using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Transkript;

public static class UpdateChecker
{
    private const string ManifestUrl =
        "https://gotfmgkrjqxzhrlagscj.supabase.co/storage/v1/object/public/releases/version.json";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public record UpdateInfo(Version Latest, string DownloadUrl, string ReleaseNotes);

    // ── Check ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the remote manifest and returns UpdateInfo if a newer version exists,
    /// null otherwise. Never throws — all errors are silently logged.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            Logger.Write("UpdateChecker : vérification des mises à jour…");

            var json = await Http.GetStringAsync(ManifestUrl);
            var doc  = JsonDocument.Parse(json).RootElement;

            string remoteStr  = doc.GetProperty("version").GetString()     ?? "0.0.0";
            string downloadUrl = doc.GetProperty("download_url").GetString() ?? "";
            string notes       = doc.TryGetProperty("release_notes", out var n)
                                 ? (n.GetString() ?? "") : "";

            var remote  = Version.Parse(remoteStr);
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

            Logger.Write($"UpdateChecker : local={current}, distant={remote}");

            if (remote > current)
                return new UpdateInfo(remote, downloadUrl, notes);

            Logger.Write("UpdateChecker : application à jour");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Write($"UpdateChecker : échec silencieux ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    // ── Download ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the installer to the system temp folder.
    /// Reports progress 0–100 via <paramref name="progress"/>.
    /// Returns the local path of the downloaded file.
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(
        string downloadUrl, IProgress<int> progress)
    {
        string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        string destPath = Path.Combine(Path.GetTempPath(), fileName);

        Logger.Write($"UpdateChecker : téléchargement → {destPath}");

        using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;

        await using var src  = await response.Content.ReadAsStreamAsync();
        await using var dest = File.Create(destPath);

        var buffer    = new byte[81920]; // 80 KB chunks
        long received = 0;
        int  read;

        while ((read = await src.ReadAsync(buffer)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read));
            received += read;

            if (total.HasValue)
                progress.Report((int)(received * 100 / total.Value));
        }

        Logger.Write($"UpdateChecker : téléchargement terminé ({received / 1024} Ko)");
        return destPath;
    }
}
