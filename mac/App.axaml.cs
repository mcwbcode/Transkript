using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Transkript.Platform;
using Transkript.Views;

namespace Transkript;

public partial class App : Application
{
    private GlobalHotkeyMac?  _hook;
    private AudioRecorderMac? _recorder;
    private Transcriber?      _transcriber;
    private MenuBarMac?       _menuBar;
    private OverlayWindow?    _overlay;

    private volatile bool _recording  = false;
    private volatile bool _processing = false;

    private AppSettings _settings = new();
    private string      _plan     = "free";

    private SettingsWindow? _settingsWin;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = Dispatcher.UIThread.InvokeAsync(StartAsync);
        }
        base.OnFrameworkInitializationCompleted();
    }

    // ── Démarrage ────────────────────────────────────────────────────────────

    private async Task StartAsync()
    {
        Logger.Init();
        Logger.Write("StartAsync");

        // ── Boucle auth + accueil ──────────────────────────────────────────
        while (true)
        {
            var session = AuthService.LoadSession();

            if (!AuthService.IsSessionValid(session))
            {
                if (session != null && !string.IsNullOrEmpty(session.RefreshToken))
                {
                    Logger.Write("Session expirée — tentative de refresh");
                    bool refreshed = await AuthService.RefreshAsync(session);
                    Logger.Write(refreshed ? "Refresh OK" : "Refresh échoué");
                    if (refreshed) session = AuthService.LoadSession();
                    else           session = null;
                }

                if (!AuthService.IsSessionValid(session))
                {
                    Logger.Write("Affichage LoginWindow");
                    var loginWin = new LoginWindow { PrefilledEmail = session?.Email ?? "" };
                    bool loginOk = await ShowWindowAsync(loginWin);
                    if (!loginOk) { Shutdown(); return; }
                    session = AuthService.LoadSession();
                }
            }

            _plan = "free";
            string plan = _plan;
            if (session != null && !string.IsNullOrEmpty(session.AccessToken))
            {
                Logger.Write("Récupération du plan…");
                plan = await AuthService.GetPlanAsync(session.AccessToken, session.UserId) ?? "free";
                _plan = plan;
                Logger.Write($"Plan : {plan}");
            }

            Logger.Write("Affichage HomeWindow");
            var homeWin = new HomeWindow();
            homeWin.SetUser(session?.Email ?? "", plan);
            bool launched = await ShowWindowAsync(homeWin);

            if (!launched)
            {
                if (homeWin.ShouldLogout)
                {
                    Logger.Write("Déconnexion — retour au login");
                    AuthService.ClearSession();
                    continue;
                }
                Logger.Write("HomeWindow fermée — fermeture app");
                Shutdown(); return;
            }

            if (plan != "pro") { Shutdown(); return; }

            Logger.Write($"Accès Pro confirmé — lancement pour {session!.Email}");
            break;
        }

        // ── Initialisation des services ────────────────────────────────────
        _settings    = AppSettings.Load();
        _overlay     = new OverlayWindow();
        _recorder    = new AudioRecorderMac();
        _transcriber = new Transcriber();
        _transcriber.SetLanguage(_settings.Language);

        _menuBar = new MenuBarMac("Transkript");
        _menuBar.ExitRequested     += () => Dispatcher.UIThread.Post(Shutdown);
        _menuBar.SettingsRequested += () => Dispatcher.UIThread.Post(OpenSettings);
        _menuBar.HistoryRequested  += () => HistoryManager.OpenHistoryFolder();
        _menuBar.UpdateRequested   += () => Dispatcher.UIThread.Post(OnUpdateRequested);

        _overlay.GetLevels = () => _recorder.WaveformLevels;

        _hook = new GlobalHotkeyMac
        {
            HotkeyCode      = _settings.HotkeyCode,
            HotkeyModifiers = _settings.HotkeyModifiers
        };
        _hook.KeyPressed  += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        _hook.Install();

        Logger.Write("Services initialisés — lancement InitWhisper + UpdateCheck");
        _ = Task.Run(InitWhisperAsync);
        _ = Task.Run(CheckForUpdateAsync);
    }

    // ── Helper fenêtre async ──────────────────────────────────────────────────

    private static Task<bool> ShowWindowAsync(Window window)
    {
        var tcs = new TaskCompletionSource<bool>();
        window.Closed += (_, _) => tcs.TrySetResult(window.Tag is true);
        window.Show();
        return tcs.Task;
    }

    // ── Initialisation Whisper ────────────────────────────────────────────────

    private async Task InitWhisperAsync()
    {
        try
        {
            Logger.Write("InitWhisperAsync : début");

            var progress = new Progress<string>(msg =>
            {
                Logger.Write($"InitWhisper progress : {msg}");
                Dispatcher.UIThread.Post(() => _menuBar!.SetStatus(msg));
            });

            await _transcriber!.InitializeAsync(progress);

            Logger.Write($"InitWhisperAsync : OK — moteur = CPU");

            Dispatcher.UIThread.Post(() =>
            {
                string key = _settings.HotkeyName;
                _menuBar!.SetStatus($"Prêt — {key} pour dicter");
            });
        }
        catch (Exception ex)
        {
            Logger.Write($"[ERREUR] InitWhisperAsync : {ex}");
        }
    }

    // ── Récupération après échec ──────────────────────────────────────────────

    private async Task RecoverTranscriberAsync()
    {
        Logger.Write("RecoverTranscriberAsync : début");
        Dispatcher.UIThread.Post(() => _menuBar!.SetStatus("Réinitialisation du moteur…"));

        try
        {
            var progress = new Progress<string>(msg =>
                Dispatcher.UIThread.Post(() => _menuBar!.SetStatus(msg)));

            await _transcriber!.ResetAsync(progress);

            Logger.Write("RecoverTranscriberAsync : OK");
            Dispatcher.UIThread.Post(() =>
                _menuBar!.SetStatus($"Prêt — {_settings.HotkeyName} pour dicter"));
        }
        catch (Exception ex)
        {
            Logger.Write($"[ERREUR] RecoverTranscriberAsync : {ex}");
            Dispatcher.UIThread.Post(() =>
                _menuBar!.SetStatus("Échec réinitialisation — relancez l'app"));
        }
    }

    // ── Événements clavier ────────────────────────────────────────────────────

    private void OnKeyPressed()
    {
        if (_recording || _processing || !_transcriber!.IsReady) return;

        _recording = true;
        Logger.Write("OnKeyPressed : début enregistrement");

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _recorder!.Start();
                _overlay!.ShowOverlay();
                _menuBar!.SetStatus("Enregistrement…");
            }
            catch (Exception ex)
            {
                Logger.Write($"[ERREUR] AudioRecorderMac.Start : {ex.Message}");
                _recording = false;
                _menuBar!.SetStatus($"Micro inaccessible : {ex.Message}");
            }
        });
    }

    private void OnKeyReleased()
    {
        if (!_recording) return;
        _recording  = false;
        _processing = true;
        Logger.Write("OnKeyReleased : arrêt enregistrement");
        Dispatcher.UIThread.Post(async () => await ProcessRecordingAsync());
    }

    // ── Pipeline de transcription ─────────────────────────────────────────────

    private async Task ProcessRecordingAsync()
    {
        _overlay!.HideOverlay();

        byte[] pcm = _recorder!.Stop();
        Logger.Write($"PCM capturé : {pcm.Length} octets ({pcm.Length / (float)(AudioRecorderMac.SampleRate * 2):F2} s)");

        float rms = AudioRecorderMac.ComputeRms(pcm);
        if (rms < 0.003f || pcm.Length < AudioRecorderMac.SampleRate * 2 / 3)
        {
            Logger.Write($"Audio ignoré : rms={rms:F4}, trop court ou silencieux");
            _menuBar!.SetStatus("Rien détecté");
            await Task.Delay(1500);
            RestoreReadyStatus();
            _processing = false;
            return;
        }

        _menuBar!.SetStatus("Transcription…");

        bool failed = false;
        try
        {
            Logger.Write("TranscribeAsync : début");
            float[] samples = AudioRecorderMac.ToFloatSamples(pcm);
            string  raw     = await Task.Run(() => _transcriber!.TranscribeAsync(samples));
            string  text    = TextProcessor.Process(raw, _settings);

            Logger.Write($"TranscribeAsync : OK — {text.Length} car.");

            if (string.IsNullOrWhiteSpace(text))
            {
                _menuBar.SetStatus("Rien détecté");
            }
            else
            {
                await Task.Delay(250);
                PasteHelperMac.Paste(text);
                int words = TextProcessor.CountWords(text);
                _menuBar.SetStatus($"✓  {words} mot{(words > 1 ? "s" : "")} ({text.Length} car.)");

                if (_settings.SaveHistory)
                    HistoryManager.Append(text, _settings.Language);
            }
        }
        catch (Exception ex)
        {
            Logger.Write($"[ERREUR] TranscribeAsync : {ex}");
            _menuBar!.SetStatus("Erreur de transcription — réinitialisation…");
            failed = true;
        }

        await Task.Delay(failed ? 500 : 2000);
        _processing = false;

        if (failed)
            _ = Task.Run(RecoverTranscriberAsync);
        else
            RestoreReadyStatus();
    }

    private void RestoreReadyStatus()
    {
        int    today = HistoryManager.GetTodayWordCount();
        string words = today > 0 ? $" · {today} mots aujourd'hui" : "";
        _menuBar?.SetStatus($"Prêt{words} — {_settings.HotkeyName} pour dicter");
    }

    // ── Paramètres ────────────────────────────────────────────────────────────

    private async void OpenSettings()
    {
        if (_settingsWin != null) { _settingsWin.Activate(); return; }

        _settingsWin = new SettingsWindow(_settings);
        _settingsWin.SetAccountPlan(_plan);
        _settingsWin.Closed += (_, _) => _settingsWin = null;

        bool saved = await ShowWindowAsync(_settingsWin);
        if (!saved) return;

        bool langChanged = _settingsWin!.NewLanguage != _settings.Language;

        _settings.HotkeyCode        = _settingsWin.NewHotkeyCode;
        _settings.HotkeyModifiers   = _settingsWin.NewHotkeyModifiers;
        _settings.HotkeyName        = _settingsWin.NewHotkeyName;
        _settings.Language           = _settingsWin.NewLanguage;
        _settings.RemoveFillers      = _settingsWin.NewRemoveFillers;
        _settings.AutoCapitalize     = _settingsWin.NewAutoCapitalize;
        _settings.RemoveDuplicates   = _settingsWin.NewRemoveDuplicates;
        _settings.SaveHistory        = _settingsWin.NewSaveHistory;
        _settings.PersonalDictionary = _settingsWin.NewPersonalDictionary;
        _settings.Save();

        _hook!.HotkeyCode      = _settings.HotkeyCode;
        _hook.HotkeyModifiers  = _settings.HotkeyModifiers;
        Logger.Write($"Paramètres mis à jour : hotkey={_settings.HotkeyName}, langue={_settings.Language}");

        if (langChanged && _transcriber!.IsReady)
        {
            _transcriber.SetLanguage(_settings.Language);
            _ = Task.Run(RecoverTranscriberAsync);
        }
        else
        {
            RestoreReadyStatus();
        }
    }

    // ── Mise à jour ───────────────────────────────────────────────────────────

    private UpdateChecker.UpdateInfo? _pendingUpdate;

    private async Task CheckForUpdateAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        var info = await UpdateChecker.CheckAsync();
        if (info == null) return;

        _pendingUpdate = info;
        Dispatcher.UIThread.Post(() =>
            _menuBar!.ShowUpdateAvailable(info.Latest.ToString(3)));
    }

    private async void OnUpdateRequested()
    {
        if (_pendingUpdate == null) return;

        _menuBar!.SetStatus("Téléchargement de la mise à jour…");

        try
        {
            var progress = new Progress<int>(pct =>
                Dispatcher.UIThread.Post(() => _menuBar!.SetStatus($"Téléchargement… {pct}%")));

            string installerPath = await Task.Run(() =>
                UpdateChecker.DownloadInstallerAsync(_pendingUpdate.DownloadUrl, progress));

            Logger.Write($"Installateur téléchargé : {installerPath}");
            System.Diagnostics.Process.Start("open", installerPath);
            Shutdown();
        }
        catch (Exception ex)
        {
            Logger.Write($"[ERREUR] OnUpdateRequested : {ex.Message}");
            _menuBar!.SetStatus("Échec du téléchargement de la mise à jour");
            RestoreReadyStatus();
        }
    }

    // ── Arrêt ─────────────────────────────────────────────────────────────────

    private void Shutdown()
    {
        Logger.Write("Shutdown");
        _hook?.Dispose();
        _recorder?.Dispose();
        _transcriber?.Dispose();
        _menuBar?.Dispose();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
