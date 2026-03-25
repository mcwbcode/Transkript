using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NAudio.Wave;
using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;

namespace Transkript;

public partial class App
{
    private Mutex? _mutex;

    private KeyboardHook?  _hook;
    private AudioRecorder? _recorder;
    private Transcriber?   _transcriber;
    private TrayManager?   _tray;
    private OverlayWindow? _overlay;

    private volatile bool _recording  = false;
    private volatile bool _processing = false;

    private AppSettings _settings = new();
    private string      _plan     = "free";

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "Transkript_v2_SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Transkript est déjà en cours d'exécution.",
                "Transkript", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Write($"[CRASH] UnhandledException : {args.ExceptionObject}");
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Write($"[CRASH] DispatcherUnhandledException : {args.Exception}");
            args.Handled = true;
        };

        Logger.Init();
        Logger.Write("OnStartup");

        base.OnStartup(e);

        // ── Boucle auth + accueil ────────────────────────────────────────────
        while (true)
        {
            // Vérification / rafraîchissement de la session
            var session = AuthService.LoadSession();

            if (!AuthService.IsSessionValid(session))
            {
                if (session != null && !string.IsNullOrEmpty(session.RefreshToken))
                {
                    Logger.Write("Session expirée — tentative de refresh");
                    bool refreshed = Task.Run(() => AuthService.RefreshAsync(session)).GetAwaiter().GetResult();
                    Logger.Write(refreshed ? "Refresh OK" : "Refresh échoué");
                    if (refreshed) session = AuthService.LoadSession();
                    else           session = null;
                }

                if (!AuthService.IsSessionValid(session))
                {
                    Logger.Write("Affichage LoginWindow");
                    var loginWin = new LoginWindow { PrefilledEmail = session?.Email ?? "" };
                    if (loginWin.ShowDialog() != true)
                    {
                        Logger.Write("Login annulé — fermeture");
                        Shutdown(); return;
                    }
                    session = AuthService.LoadSession();
                }
            }

            // Récupération du plan
            _plan = "free";
            string plan = _plan;
            if (session != null && !string.IsNullOrEmpty(session.AccessToken))
            {
                Logger.Write("Récupération du plan…");
                plan = Task.Run(() => AuthService.GetPlanAsync(session.AccessToken, session.UserId))
                           .GetAwaiter().GetResult() ?? "free";
                _plan = plan;
                Logger.Write($"Plan : {plan}");
            }

            // Fenêtre d'accueil
            Logger.Write("Affichage HomeWindow");
            var homeWin = new HomeWindow();
            homeWin.SetUser(session?.Email ?? "", plan);

            if (homeWin.ShowDialog() != true)
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

            // Seul le plan Pro peut lancer l'app (double vérification)
            if (plan != "pro")
            {
                Shutdown(); return;
            }

            Logger.Write($"Accès Pro confirmé — lancement pour {session!.Email}");
            break;
        }

        // ── Initialisation des services ──────────────────────────────────────
        _settings = AppSettings.Load();
        Logger.Write($"Hotkey chargé : {_settings.HotkeyName} (VK {_settings.HotkeyVk})");
        Logger.Write($"Langue : {_settings.Language} | FiltreMots : {_settings.RemoveFillers} | Historique : {_settings.SaveHistory}");

        _overlay     = new OverlayWindow();
        _recorder    = new AudioRecorder();
        _transcriber = new Transcriber();
        _transcriber.SetLanguage(_settings.Language);

        _tray = new TrayManager();
        _tray.ExitRequested      += () => Dispatcher.Invoke(Shutdown);
        _tray.SettingsRequested  += () => Dispatcher.Invoke(OpenSettings);
        _tray.HistoryRequested   += () => HistoryManager.OpenHistoryFolder();

        _overlay.GetLevels = () => _recorder.WaveformLevels;

        _hook = new KeyboardHook { HotkeyVk = _settings.HotkeyVk };
        _hook.KeyPressed  += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        _hook.Install();

        _tray.UpdateRequested += () => Dispatcher.Invoke(OnUpdateRequested);

        Logger.Write("Services initialisés — lancement InitWhisper + UpdateCheck");
        _ = Task.Run(InitWhisperAsync);
        _ = Task.Run(CheckForUpdateAsync);
    }

    // ── Model initialisation ─────────────────────────────────────────────────

    private async Task InitWhisperAsync()
    {
        try
        {
            Logger.Write("InitWhisperAsync : début");

            var progress = new Progress<string>(msg =>
            {
                Logger.Write($"InitWhisper progress : {msg}");
                Dispatcher.Invoke(() => _tray!.SetStatus(msg));
            });

            await _transcriber!.InitializeAsync(progress);

            Logger.Write($"InitWhisperAsync : OK — moteur = {(_transcriber.UsingCuda ? "CUDA" : "CPU")}");

            Dispatcher.Invoke(() =>
            {
                string key = _settings.HotkeyName;
                _tray!.SetStatus($"Prêt — {key} pour dicter");
                _tray.ShowBalloon("Transkript prêt", $"Maintenez {key} pour commencer à dicter.");
            });
        }
        catch (Exception ex)
        {
            Logger.Write($"[ERREUR] InitWhisperAsync : {ex}");
            Dispatcher.Invoke(() =>
                MessageBox.Show(
                    $"Erreur lors du chargement du modèle Whisper :\n\n{ex.Message}",
                    "Transkript — Erreur", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    // ── Auto-recovery after transcription failure ────────────────────────────

    private async Task RecoverTranscriberAsync()
    {
        Logger.Write("RecoverTranscriberAsync : début");
        Dispatcher.Invoke(() => _tray!.SetStatus("Réinitialisation du moteur…"));

        try
        {
            var progress = new Progress<string>(msg =>
                Dispatcher.Invoke(() => _tray!.SetStatus(msg)));

            await _transcriber!.ResetAsync(progress);

            Logger.Write("RecoverTranscriberAsync : OK");
            Dispatcher.Invoke(() =>
            {
                _tray!.SetStatus($"Prêt — {_settings.HotkeyName} pour dicter");
                _tray.ShowBalloon("Transkript", "Moteur réinitialisé avec succès.");
            });
        }
        catch (Exception ex)
        {
            Logger.Write($"[ERREUR] RecoverTranscriberAsync : {ex}");
            Dispatcher.Invoke(() =>
                _tray!.SetStatus("Échec réinitialisation — relancez l'app"));
        }
    }

    // ── Keyboard events ──────────────────────────────────────────────────────

    private void OnKeyPressed()
    {
        if (_recording || _processing || !_transcriber!.IsReady) return;

        _recording = true;
        Logger.Write("OnKeyPressed : début enregistrement");

        PlayStartSound();

        Dispatcher.Invoke(() =>
        {
            try
            {
                _recorder!.Start();
                _overlay!.ShowOverlay();
                _tray!.SetStatus("Enregistrement…");
            }
            catch (Exception ex)
            {
                Logger.Write($"[ERREUR] AudioRecorder.Start : {ex.Message}");
                _recording = false;
                _tray!.SetStatus($"Micro inaccessible : {ex.Message}");
            }
        });
    }

    private void OnKeyReleased()
    {
        if (!_recording) return;
        _recording  = false;
        _processing = true;
        Logger.Write("OnKeyReleased : arrêt enregistrement");
        Dispatcher.InvokeAsync(ProcessRecordingAsync);
    }

    // ── Son de démarrage ─────────────────────────────────────────────────────

    private static void PlayStartSound()
    {
        Task.Run(() =>
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(
                    "Transkript.mixkit-opening-software-interface-2578.wav")!;
                using var reader = new WaveFileReader(stream);
                using var output = new WaveOutEvent();
                output.Volume = 0.10f;
                output.Init(reader);
                output.Play();
                while (output.PlaybackState == PlaybackState.Playing)
                    Thread.Sleep(20);
            }
            catch { }
        });
    }

    // ── Transcription pipeline ───────────────────────────────────────────────

    private async Task ProcessRecordingAsync()
    {
        _overlay!.HideOverlay();

        byte[] pcm = _recorder!.Stop();
        Logger.Write($"PCM capturé : {pcm.Length} octets ({pcm.Length / (float)(AudioRecorder.SampleRate * 2):F2} s)");

        float rms = AudioRecorder.ComputeRms(pcm);
        if (rms < 0.003f || pcm.Length < AudioRecorder.SampleRate * 2 / 3)
        {
            Logger.Write($"Audio ignoré : rms={rms:F4}, trop court ou silencieux");
            _tray!.SetStatus("Rien détecté");
            await Task.Delay(1500);
            RestoreReadyStatus();
            _processing = false;
            return;
        }

        _tray!.SetStatus("Transcription…");

        bool transcriptionFailed = false;
        try
        {
            Logger.Write("TranscribeAsync : début");
            float[] samples = AudioRecorder.ToFloatSamples(pcm);
            Logger.Write($"TranscribeAsync : {samples.Length} samples envoyés au modèle");

            string raw  = await Task.Run(() => _transcriber!.TranscribeAsync(samples));
            string text = TextProcessor.Process(raw, _settings);

            Logger.Write($"TranscribeAsync : OK — brut={raw.Length} car, traité={text.Length} car");

            if (string.IsNullOrWhiteSpace(text))
            {
                _tray.SetStatus("Rien détecté");
            }
            else
            {
                await Task.Delay(250); // laisser Windows rendre le focus à l'app cible
                PasteHelper.Paste(text);
                int words = TextProcessor.CountWords(text);
                _tray.SetStatus($"✓  {words} mot{(words > 1 ? "s" : "")} ({text.Length} car.)");

                // Save to history
                if (_settings.SaveHistory)
                    HistoryManager.Append(text, _settings.Language);
            }
        }
        catch (Exception ex)
        {
            Logger.Write($"[ERREUR] TranscribeAsync : {ex}");
            _tray!.SetStatus("Erreur de transcription — réinitialisation…");
            transcriptionFailed = true;
        }

        await Task.Delay(transcriptionFailed ? 500 : 2000);
        _processing = false;

        // Auto-recover: reinitialize processor in background after failure
        if (transcriptionFailed)
            _ = Task.Run(RecoverTranscriberAsync);
        else
            RestoreReadyStatus();
    }

    private void RestoreReadyStatus()
    {
        int    today = HistoryManager.GetTodayWordCount();
        string words = today > 0 ? $" · {today} mots aujourd'hui" : "";
        _tray?.SetStatus($"Prêt{words} — {_settings.HotkeyName} pour dicter");
    }

    // ── Paramètres ───────────────────────────────────────────────────────────

    private void OpenSettings()
    {
        var win = new SettingsWindow(_settings);
        win.SetAccountPlan(_plan);
        if (win.ShowDialog() != true) return;

        bool languageChanged = win.NewLanguage != _settings.Language;

        _settings.HotkeyVk          = win.NewHotkeyVk;
        _settings.HotkeyName        = win.NewHotkeyName;
        _settings.Language           = win.NewLanguage;
        _settings.RemoveFillers      = win.NewRemoveFillers;
        _settings.AutoCapitalize     = win.NewAutoCapitalize;
        _settings.RemoveDuplicates   = win.NewRemoveDuplicates;
        _settings.SaveHistory        = win.NewSaveHistory;
        _settings.PersonalDictionary = win.NewPersonalDictionary;
        _settings.Save();

        _hook!.HotkeyVk = _settings.HotkeyVk;
        Logger.Write($"Paramètres mis à jour : hotkey={_settings.HotkeyName}, langue={_settings.Language}");

        // Re-init Whisper with new language if needed
        if (languageChanged && _transcriber!.IsReady)
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
        // Attendre 10 s après le démarrage pour ne pas surcharger le démarrage
        await Task.Delay(TimeSpan.FromSeconds(10));

        var info = await UpdateChecker.CheckAsync();
        if (info == null) return;

        _pendingUpdate = info;
        Dispatcher.Invoke(() =>
            _tray!.ShowUpdateAvailable(info.Latest.ToString(3)));
    }

    private async void OnUpdateRequested()
    {
        if (_pendingUpdate == null) return;

        _tray!.SetStatus("Téléchargement de la mise à jour…");

        try
        {
            var progress = new Progress<int>(pct =>
                Dispatcher.Invoke(() => _tray!.SetStatus($"Téléchargement… {pct}%")));

            string installerPath = await Task.Run(() =>
                UpdateChecker.DownloadInstallerAsync(_pendingUpdate.DownloadUrl, progress));

            var result = MessageBox.Show(
                $"Transkript {_pendingUpdate.Latest.ToString(3)} est prêt à installer.\n\n" +
                "L'application va se fermer pour lancer l'installation.",
                "Mise à jour prête",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.OK) return;

            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            Shutdown();
        }
        catch (Exception ex)
        {
            Logger.Write($"[ERREUR] OnUpdateRequested : {ex.Message}");
            _tray!.SetStatus("Échec du téléchargement de la mise à jour");
            MessageBox.Show(
                $"Le téléchargement a échoué :\n{ex.Message}",
                "Transkript", MessageBoxButton.OK, MessageBoxImage.Warning);
            RestoreReadyStatus();
        }
    }

    // ── Shutdown ─────────────────────────────────────────────────────────────

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Write("OnExit");
        _hook?.Dispose();
        _recorder?.Dispose();
        _transcriber?.Dispose();
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
