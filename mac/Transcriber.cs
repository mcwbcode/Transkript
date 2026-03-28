using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Transkript.Platform;
using Whisper.net;
using Whisper.net.Ggml;

namespace Transkript;

/// <summary>
/// Wraps Whisper.net pour transcrire des samples PCM float[].
/// Modèle : small (~488 Mo). CPU uniquement sur macOS.
/// </summary>
public sealed class Transcriber : IDisposable
{
    private const GgmlType         ModelType  = GgmlType.Small;
    private const QuantizationType ModelQuant = QuantizationType.NoQuantization;
    private const string           ModelFile  = "ggml-small.bin";

    private static readonly string ModelDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "Transkript", "models");

    private static readonly string ModelPath = Path.Combine(ModelDir, ModelFile);

    private WhisperFactory?   _factory;
    private WhisperProcessor? _processor;

    private string _language = "fr";
    private string _prompt   = "Transcription en français. Voici un texte dicté :";

    public bool IsReady { get; private set; }

    public void SetLanguage(string languageCode)
    {
        _language = languageCode == "auto" ? "auto" : languageCode;
        _prompt   = languageCode switch
        {
            "fr"   => "Transcription en français. Voici un texte dicté :",
            "en"   => "English transcription. Dictated text follows:",
            "es"   => "Transcripción en español. Texto dictado:",
            "de"   => "Transkription auf Deutsch. Diktierter Text:",
            "it"   => "Trascrizione in italiano. Testo dettato:",
            "pt"   => "Transcrição em português. Texto ditado:",
            "nl"   => "Transcriptie in het Nederlands. Gedicteerde tekst:",
            "auto" => "",
            _      => ""
        };
    }

    public async Task ResetAsync(IProgress<string>? progress = null)
    {
        IsReady = false;
        Logger.Write("ResetAsync : début réinitialisation du processeur");

        _processor?.Dispose(); _processor = null;
        _factory?.Dispose();   _factory   = null;

        await InitializeAsync(progress);
    }

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

        int threads = Math.Max(4, Environment.ProcessorCount);

        try
        {
            Logger.Write("CPU : WhisperFactory.FromPath");
            _factory = WhisperFactory.FromPath(ModelPath);

            Logger.Write("CPU : CreateBuilder");
            _processor = _factory.CreateBuilder()
                .WithLanguage(_language)
                .WithNoContext()
                .WithThreads(threads)
                .WithPrompt(_prompt)
                .Build();

            progress?.Report("Initialisation CPU…");
            Logger.Write("CPU : WarmUp");
            await WarmUpAsync();
            Logger.Write("CPU : OK");
        }
        catch (Exception ex)
        {
            Logger.Write($"CPU ÉCHEC ({ex.GetType().Name} : {ex.Message})");
            _processor?.Dispose(); _processor = null;
            _factory?.Dispose();   _factory   = null;
            throw;
        }

        IsReady = true;
        Logger.Write("InitializeAsync terminé — IsReady=true");
    }

    private async Task WarmUpAsync()
    {
        var silence = new float[AudioRecorderMac.SampleRate / 2];
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
