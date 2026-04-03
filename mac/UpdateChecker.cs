using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            Logger.Write("UpdateChecker : vérification des mises à jour…");

            var json = await Http.GetStringAsync(ManifestUrl);
            var doc  = JsonDocument.Parse(json).RootElement;

            string remoteStr = doc.GetProperty("version").GetString()      ?? "0.0.0";
            string notes     = doc.TryGetProperty("release_notes", out var n)
                               ? (n.GetString() ?? "") : "";

            // Use mac_download_url when present, fall back to download_url
            string downloadUrl = "";
            if (doc.TryGetProperty("mac_download_url", out var macUrl))
                downloadUrl = macUrl.GetString() ?? "";
            if (string.IsNullOrEmpty(downloadUrl))
                downloadUrl = doc.GetProperty("download_url").GetString() ?? "";

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

        var  buffer   = new byte[81920];
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

    // ── Auto-install (macOS DMG) ───────────────────────────────────────────────

    /// <summary>
    /// Mounts the DMG, copies Transkript.app over the running installation,
    /// unmounts the disk image, then returns the installed app path for relaunch.
    /// </summary>
    public static async Task<string> AutoInstallMacAsync(
        string dmgPath, IProgress<string> progress)
    {
        // 1 — Mount the DMG silently
        progress.Report("Montage de la mise à jour…");
        Logger.Write($"AutoInstall: montage de {dmgPath}");

        string attachOut = await RunShellAsync(
            $"hdiutil attach \"{dmgPath}\" -nobrowse -noverify -noautoopen 2>&1");
        Logger.Write($"AutoInstall: hdiutil attach → {attachOut.Trim()}");

        // Parse mount point — last /Volumes/… path on the last line
        string mountPoint = ParseMountPoint(attachOut);
        if (string.IsNullOrEmpty(mountPoint))
            throw new Exception($"Impossible de monter le DMG (sortie: {attachOut})");

        Logger.Write($"AutoInstall: volume monté → {mountPoint}");

        try
        {
            // 2 — Find the .app bundle in the mounted volume
            string[] apps = Directory.GetDirectories(mountPoint, "*.app",
                                SearchOption.TopDirectoryOnly);
            if (apps.Length == 0)
                throw new Exception($"Aucun .app trouvé dans {mountPoint}");

            string srcApp  = apps[0];
            string appName = Path.GetFileName(srcApp);     // e.g. Transkript.app
            string destApp = $"/Applications/{appName}";

            Logger.Write($"AutoInstall: copie {srcApp} → {destApp}");
            progress.Report("Installation…");

            // 3 — Replace with rsync (preserves permissions, no admin needed for /Applications)
            await RunShellAsync(
                $"rsync -a --delete \"{srcApp}/\" \"{destApp}/\" 2>&1");

            Logger.Write($"AutoInstall: copie terminée");
            return destApp;
        }
        finally
        {
            // 4 — Always unmount, even on error
            progress.Report("Finalisation…");
            await RunShellAsync($"hdiutil detach \"{mountPoint}\" -quiet 2>/dev/null || true");
            Logger.Write("AutoInstall: volume démonté");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ParseMountPoint(string hdiutilOutput)
    {
        // hdiutil attach outputs lines like:
        //   /dev/disk4s1    Apple_HFS    /Volumes/Transkript 1.0.1
        var match = Regex.Match(hdiutilOutput, @"/Volumes/[^\n]+");
        return match.Success ? match.Value.Trim() : string.Empty;
    }

    private static async Task<string> RunShellAsync(string command)
    {
        var psi = new ProcessStartInfo("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };

        using var proc = Process.Start(psi)
            ?? throw new Exception($"Impossible de lancer : {command}");

        string output = await proc.StandardOutput.ReadToEndAsync();
        string errors = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        return output + errors;
    }
}
