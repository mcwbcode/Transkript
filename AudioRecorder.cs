using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;

namespace Transkript;

/// <summary>
/// Captures microphone audio at 16 kHz mono PCM-16 via NAudio.
/// Also computes per-bar amplitude levels for the waveform overlay.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    public const int SampleRate = 16_000;
    public const int Channels   = 1;
    public const int BitDepth   = 16;

    private const int WaveformBars = 20;
    private const float AttackCoef = 1.0f;   // instant attack
    private const float DecayCoef  = 0.75f;  // slow decay

    private WaveInEvent?     _waveIn;
    private List<byte>       _buffer = new();
    private readonly object  _lock   = new();

    /// <summary>Smoothed per-bar amplitude levels in [0, 1]. Read from any thread.</summary>
    public float[] WaveformLevels { get; } = new float[WaveformBars];

    public void Start()
    {
        lock (_lock)
        {
            _buffer = new List<byte>(SampleRate * 2 * 30); // pre-alloc 30s
            Array.Clear(WaveformLevels, 0, WaveformBars);
        }

        // DeviceNumber = -1 → WAVE_MAPPER : Windows choisit le micro par défaut
        _waveIn = new WaveInEvent
        {
            DeviceNumber       = -1,
            WaveFormat         = new WaveFormat(SampleRate, BitDepth, Channels),
            BufferMilliseconds = 40,
        };
        _waveIn.DataAvailable += OnData;
        _waveIn.StartRecording();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            _buffer.AddRange(new ReadOnlySpan<byte>(e.Buffer, 0, e.BytesRecorded).ToArray());
        }

        // Compute per-bar RMS from the fresh chunk (PCM-16 → float)
        int sampleCount = e.BytesRecorded / 2;
        if (sampleCount == 0) return;

        int samplesPerBar = Math.Max(1, sampleCount / WaveformBars);

        for (int bar = 0; bar < WaveformBars; bar++)
        {
            int start = bar * samplesPerBar;
            int end   = Math.Min(start + samplesPerBar, sampleCount);

            double sum = 0;
            for (int i = start; i < end; i++)
            {
                float s = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
                sum += s * s;
            }

            float rms    = (float)Math.Sqrt(sum / (end - start));
            float target = Math.Min(1f, rms * 6f); // amplify for visual impact

            // Smooth: fast attack, slow decay
            float cur = WaveformLevels[bar];
            WaveformLevels[bar] = target > cur
                ? target * AttackCoef + cur * (1f - AttackCoef)
                : cur * DecayCoef;
        }
    }

    /// <summary>Stops recording and returns raw PCM-16 bytes at 16 kHz mono.</summary>
    public byte[] Stop()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        lock (_lock)
            return _buffer.ToArray();
    }

    /// <summary>Converts raw PCM-16 bytes to normalised float samples for Whisper.</summary>
    public static float[] ToFloatSamples(byte[] pcm)
    {
        float[] samples = new float[pcm.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
        return samples;
    }

    /// <summary>
    /// Supprime le silence en début et fin de tableau de samples.
    /// Garde 200 ms de marge autour de la parole détectée.
    /// </summary>
    public static float[] TrimSilence(float[] samples, float threshold = 0.01f)
    {
        const int windowSize = 800;   // 50 ms à 16 kHz
        const int padding    = 3200;  // 200 ms de marge

        int first = 0;
        for (int i = 0; i + windowSize <= samples.Length; i += windowSize / 2)
        {
            double rms = 0;
            for (int j = i; j < i + windowSize; j++) rms += samples[j] * samples[j];
            if (Math.Sqrt(rms / windowSize) >= threshold) { first = i; break; }
        }

        int last = samples.Length;
        for (int i = samples.Length - windowSize; i >= 0; i -= windowSize / 2)
        {
            double rms = 0;
            for (int j = i; j < i + windowSize; j++) rms += samples[j] * samples[j];
            if (Math.Sqrt(rms / windowSize) >= threshold) { last = i + windowSize; break; }
        }

        int start = Math.Max(0, first - padding);
        int end   = Math.Min(samples.Length, last + padding);

        if (end <= start) return samples; // audio trop court, on garde tout

        var trimmed = new float[end - start];
        Array.Copy(samples, start, trimmed, 0, trimmed.Length);
        return trimmed;
    }

    /// <summary>Returns the RMS level of the audio (0 = silence, 1 = max).</summary>
    public static float ComputeRms(byte[] pcm)
    {
        if (pcm.Length < 2) return 0f;
        double sum = 0;
        int n = pcm.Length / 2;
        for (int i = 0; i < n; i++)
        {
            float s = BitConverter.ToInt16(pcm, i * 2) / 32768f;
            sum += s * s;
        }
        return (float)Math.Sqrt(sum / n);
    }

    public void Dispose()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
    }
}
