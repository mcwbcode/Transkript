using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace Transkript;

/// <summary>
/// Wraps Whisper.net pour transcrire des samples PCM float[].
/// Modèle : small (~488 Mo). Tente CUDA, fallback CPU si indisponible.
/// </summary>
public sealed class Transcriber : IDisposable
{
    private const GgmlType        ModelType  = GgmlType.Small;
    private const QuantizationType ModelQuant = QuantizationType.NoQuantization;
    private const string           ModelFile  = "ggml-small.bin";

    private static readonly string ModelDir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SuperTranscript", "models");

    private static readonly string ModelPath = Path.Combine(ModelDir, ModelFile);

    private WhisperFactory?   _factory;
    private WhisperProcessor? _processor;

    public bool IsReady     { get; private set; }
    public bool UsingCuda   { get; private set; }

    public async Task InitializeAsync(IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(ModelDir);

        if (!File.Exists(ModelPath))
        {
            progress?.Report("Téléchargement du modèle Whisper (~488 Mo)…");
            await using var stream = await WhisperGgmlDownloader.GetGgmlModelAsync(ModelType, ModelQuant);
            await using var file   = File.OpenWrite(ModelPath);
            await stream.CopyToAsync(file);
        }

        progress?.Report("Chargement du modèle…");

        // Ajouter CUDA bin au PATH si une installation est détectée
        AddCudaToPath();

        int threads = Math.Max(4, Environment.ProcessorCount);

        // ── Tentative CUDA (FromPath + Build + WarmUp dans le même try) ───────
        try
        {
            Logger.Write("Tentative CUDA : SetRuntimeLibraryOrder");
            RuntimeOptions.Instance.SetRuntimeLibraryOrder(
                [RuntimeLibrary.Cuda, RuntimeLibrary.Cpu]);

            Logger.Write("Tentative CUDA : WhisperFactory.FromPath");
            _factory   = WhisperFactory.FromPath(ModelPath);

            Logger.Write("Tentative CUDA : CreateBuilder");
            _processor = _factory.CreateBuilder()
                .WithLanguage("fr")
                .WithNoContext()
                .WithThreads(threads)
                .WithPrompt("Transcription en français. Voici un texte dicté :")
                .Build();

            progress?.Report("Initialisation GPU…");
            Logger.Write("Tentative CUDA : WarmUp");
            await WarmUpAsync();

            Logger.Write("Tentative CUDA : OK");
            UsingCuda = true;
        }
        catch (Exception ex)
        {
            Logger.Write($"Tentative CUDA : ÉCHEC ({ex.GetType().Name} : {ex.Message}) → fallback CPU");
            _processor?.Dispose(); _processor = null;
            _factory?.Dispose();   _factory   = null;
            UsingCuda = false;
        }

        // ── Fallback CPU ──────────────────────────────────────────────────────
        if (!UsingCuda)
        {
            progress?.Report("Initialisation CPU…");
            Logger.Write("Fallback CPU : SetRuntimeLibraryOrder");
            RuntimeOptions.Instance.SetRuntimeLibraryOrder([RuntimeLibrary.Cpu]);

            Logger.Write("Fallback CPU : WhisperFactory.FromPath");
            _factory   = WhisperFactory.FromPath(ModelPath);

            Logger.Write("Fallback CPU : CreateBuilder");
            _processor = _factory.CreateBuilder()
                .WithLanguage("fr")
                .WithNoContext()
                .WithThreads(threads)
                .WithPrompt("Transcription en français. Voici un texte dicté :")
                .Build();

            Logger.Write("Fallback CPU : WarmUp");
            await WarmUpAsync();
            Logger.Write("Fallback CPU : OK");
        }

        IsReady = true;
        Logger.Write($"InitializeAsync terminé — IsReady=true, UsingCuda={UsingCuda}");
    }

    private static void AddCudaToPath()
    {
        const string baseDir = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
        if (!Directory.Exists(baseDir)) return;

        var bin = Directory.GetDirectories(baseDir)
            .Where(d => Path.GetFileName(d).StartsWith("v12"))
            .OrderByDescending(d => d)
            .Select(d => Path.Combine(d, "bin"))
            .FirstOrDefault(Directory.Exists);

        if (bin == null) return;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!path.Contains(bin))
            Environment.SetEnvironmentVariable("PATH", bin + ";" + path);
    }

    private async Task WarmUpAsync()
    {
        var silence = new float[AudioRecorder.SampleRate / 2]; // 0.5 s de silence
        await foreach (var _ in _processor!.ProcessAsync(silence)) { }
    }

    public async Task<string> TranscribeAsync(float[] samples)
    {
        if (_processor == null)
            throw new InvalidOperationException("Transcriber non initialisé.");

        var sb = new StringBuilder();
        await foreach (var segment in _processor.ProcessAsync(samples))
            sb.Append(segment.Text);

        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
