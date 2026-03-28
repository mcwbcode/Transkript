using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Transkript;

public class SessionData
{
    [JsonPropertyName("access_token")]  public string   AccessToken  { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string   RefreshToken { get; set; } = "";
    [JsonPropertyName("email")]         public string   Email        { get; set; } = "";
    [JsonPropertyName("user_id")]       public string   UserId       { get; set; } = "";
    [JsonPropertyName("expires_at")]    public DateTime ExpiresAt    { get; set; }
}

public static class AuthService
{
    private const string Url = "https://gotfmgkrjqxzhrlagscj.supabase.co";
    private const string Key = "sb_publishable_6ihonkWVKPbA-jeTcp5QBw_QU1fVcI5";

    private static readonly HttpClient Http = new();

    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "Transkript", "auth.json");

    // ── Sign in ──────────────────────────────────────────────────────────────

    public static async Task<(bool ok, string error)> SignInAsync(string email, string password)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { email, password });
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{Url}/auth/v1/token?grant_type=password");
            req.Headers.Add("apikey", Key);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            Logger.Write($"SignIn HTTP {(int)resp.StatusCode}");

            if (!resp.IsSuccessStatusCode)
                return (false, ParseError(json));

            var doc    = JsonDocument.Parse(json).RootElement;
            var token  = doc.GetProperty("access_token").GetString() ?? "";
            var userId = doc.GetProperty("user").GetProperty("id").GetString() ?? "";
            SaveSession(new SessionData
            {
                AccessToken  = token,
                RefreshToken = doc.GetProperty("refresh_token").GetString() ?? "",
                Email        = email,
                UserId       = userId,
                ExpiresAt    = DateTime.UtcNow.AddSeconds(
                    doc.GetProperty("expires_in").GetInt32())
            });

            _ = UpdateProfileAsync(token, userId);

            return (true, "");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ── Sign up ──────────────────────────────────────────────────────────────

    public static async Task<(bool ok, string message, bool needsConfirmation)> SignUpAsync(
        string email, string password)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { email, password });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Url}/auth/v1/signup");
            req.Headers.Add("apikey", Key);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            Logger.Write($"SignUp HTTP {(int)resp.StatusCode}");

            if (!resp.IsSuccessStatusCode)
                return (false, ParseError(json), false);

            var doc = JsonDocument.Parse(json).RootElement;

            if (doc.TryGetProperty("access_token", out var at) && at.GetString() != null)
            {
                var signupToken  = at.GetString()!;
                var signupUserId = doc.GetProperty("user").GetProperty("id").GetString() ?? "";
                SaveSession(new SessionData
                {
                    AccessToken  = signupToken,
                    RefreshToken = doc.GetProperty("refresh_token").GetString() ?? "",
                    Email        = email,
                    UserId       = signupUserId,
                    ExpiresAt    = DateTime.UtcNow.AddSeconds(
                        doc.GetProperty("expires_in").GetInt32())
                });
                _ = UpdateProfileAsync(signupToken, signupUserId);
                return (true, "", false);
            }

            return (true, "Vérifie ta boîte mail pour confirmer ton inscription.", true);
        }
        catch (Exception ex) { return (false, ex.Message, false); }
    }

    // ── Profil ───────────────────────────────────────────────────────────────

    public static async Task UpdateProfileAsync(string accessToken, string userId)
    {
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                last_login  = DateTime.UtcNow,
                app_version = "1.0.0"
            });

            using var req = new HttpRequestMessage(HttpMethod.Patch,
                $"{Url}/rest/v1/profiles?id=eq.{userId}");
            req.Headers.Add("apikey", Key);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            await Http.SendAsync(req);
        }
        catch { }
    }

    public static async Task<string?> GetPlanAsync(string accessToken, string userId)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{Url}/rest/v1/licences?select=plan,valid_until&user_id=eq.{userId}&limit=1");
            req.Headers.Add("apikey", Key);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var arr = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            if (arr.GetArrayLength() == 0) return null;

            var row = arr[0];
            if (row.TryGetProperty("valid_until", out var vu)
                && vu.ValueKind != JsonValueKind.Null
                && DateTime.TryParse(vu.GetString(), out var exp)
                && exp < DateTime.UtcNow)
                return null;

            return row.GetProperty("plan").GetString();
        }
        catch { return null; }
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    public static async Task<bool> RefreshAsync(SessionData s)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { refresh_token = s.RefreshToken });
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{Url}/auth/v1/token?grant_type=refresh_token");
            req.Headers.Add("apikey", Key);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return false;

            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            s.AccessToken  = doc.GetProperty("access_token").GetString()  ?? "";
            s.RefreshToken = doc.GetProperty("refresh_token").GetString() ?? "";
            s.ExpiresAt    = DateTime.UtcNow.AddSeconds(doc.GetProperty("expires_in").GetInt32());
            if (doc.TryGetProperty("user", out var u))
                s.UserId = u.GetProperty("id").GetString() ?? s.UserId;
            SaveSession(s);
            return true;
        }
        catch { return false; }
    }

    // ── Session helpers ──────────────────────────────────────────────────────

    public static SessionData? LoadSession()
    {
        if (!File.Exists(SessionPath)) return null;
        try   { return JsonSerializer.Deserialize<SessionData>(File.ReadAllText(SessionPath)); }
        catch { return null; }
    }

    public static bool IsSessionValid(SessionData? s)
        => s != null
        && !string.IsNullOrEmpty(s.AccessToken)
        && s.ExpiresAt > DateTime.UtcNow.AddMinutes(5);

    public static void ClearSession()
    {
        try { if (File.Exists(SessionPath)) File.Delete(SessionPath); } catch { }
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private static void SaveSession(SessionData s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
        File.WriteAllText(SessionPath,
            JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ParseError(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json).RootElement;
            foreach (var key in new[] { "error_description", "msg", "message", "error" })
                if (doc.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString() ?? "Erreur inconnue";
        }
        catch { }
        return "Erreur de connexion";
    }
}
