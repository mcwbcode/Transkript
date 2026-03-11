using System;
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
            string plan = "free";
            if (session != null && !string.IsNullOrEmpty(session.AccessToken))
            {
                Logger.Write("Récupération du plan…");
                plan = Task.Run(() => AuthService.GetPlanAsync(session.AccessToken, session.UserId))
                           .GetAwaiter().GetResult() ?? "free";
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

        _overlay     = new OverlayWindow();
        _recorder    = new AudioRecorder();
        _transcriber = new Transcriber();

        _tray = new TrayManager();
        _tray.ExitRequested     += () => Dispatcher.Invoke(Shutdown);
        _tray.SettingsRequested += () => Dispatcher.Invoke(OpenSettings);

        _overlay.GetLevels = () => _recorder.WaveformLevels;

        _hook = new KeyboardHook { HotkeyVk = _settings.HotkeyVk };
        _hook.KeyPressed  += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        _hook.Install();

        Logger.Write("Services initialisés — lancement InitWhisper");
        _ = Task.Run(InitWhisperAsync);
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
                string mode = _transcriber!.UsingCuda ? "GPU (CUDA)" : "CPU";
                string key  = _settings.HotkeyName;
                _tray!.SetStatus($"Prêt [{mode}] — maintenez {key} pour dicter");
                _tray.ShowBalloon("Transkript prêt",
                    $"Moteur : {mode}. Maintenez {key} pour commencer à dicter.");
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
        if (rms < 0.008f || pcm.Length < AudioRecorder.SampleRate * 2 / 3)
        {
            Logger.Write($"Audio ignoré : rms={rms:F4}, trop court ou silencieux");
            _tray!.SetStatus("Rien détecté");
            await Task.Delay(1500);
            RestoreReadyStatus();
            _processing = false;
            return;
        }

        _tray!.SetStatus("Transcription…");

        try
        {
            Logger.Write("TranscribeAsync : début");
            float[] samples = AudioRecorder.ToFloatSamples(pcm);
            Logger.Write($"TranscribeAsync : {samples.Length} samples envoyés au modèle");

            string text = await Task.Run(() => _transcriber!.TranscribeAsync(samples));

            Logger.Write($"TranscribeAsync : OK — {text.Length} caractères");

            if (string.IsNullOrWhiteSpace(text))
                _tray.SetStatus("Rien détecté");
            else
            {
                PasteHelper.Paste(text);
                _tray.SetStatus($"✓  {text.Length} caractères collés");
            }
        }
        catch (Exception ex)
        {
            Logger.Write($"[ERREUR] TranscribeAsync : {ex}");
            _tray!.SetStatus($"Erreur : {ex.Message}");
        }

        await Task.Delay(2000);
        RestoreReadyStatus();
        _processing = false;
    }

    private void RestoreReadyStatus()
    {
        string mode = _transcriber?.UsingCuda == true ? "GPU (CUDA)" : "CPU";
        _tray?.SetStatus($"Prêt [{mode}] — maintenez {_settings.HotkeyName} pour dicter");
    }

    // ── Paramètres ───────────────────────────────────────────────────────────

    private void OpenSettings()
    {
        var win = new SettingsWindow(_settings);
        if (win.ShowDialog() != true) return;

        _settings.HotkeyVk   = win.NewHotkeyVk;
        _settings.HotkeyName = win.NewHotkeyName;
        _settings.Save();

        _hook!.HotkeyVk = _settings.HotkeyVk;
        Logger.Write($"Hotkey mis à jour : {_settings.HotkeyName} (VK {_settings.HotkeyVk})");
        RestoreReadyStatus();
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
