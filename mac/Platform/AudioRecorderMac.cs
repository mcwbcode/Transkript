using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Transkript.Platform;

/// <summary>
/// Captures microphone audio at 16 kHz mono PCM-16 via macOS AudioQueue (CoreAudio).
/// Equivalent of the Windows NAudio-based AudioRecorder.
/// </summary>
public sealed class AudioRecorderMac : IDisposable
{
    public const int SampleRate  = 16_000;
    public const int Channels    = 1;
    public const int BitDepth    = 16;

    private const int WaveformBars = 20;
    private const float DecayCoef  = 0.75f;

    // ── AudioQueue P/Invoke ───────────────────────────────────────────────────
    // CoreAudio is in /System/Library/Frameworks/CoreAudio.framework/CoreAudio
    private const string CoreAudioLib = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string AudioToolboxLib = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    private delegate void AudioQueueInputCallback(
        IntPtr inUserData,
        IntPtr inAQ,
        IntPtr inBuffer,
        ref AudioTimeStamp inStartTime,
        uint inNumberPacketDescriptions,
        IntPtr inPacketDescs);

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStreamBasicDescription
    {
        public double mSampleRate;
        public uint   mFormatID;
        public uint   mFormatFlags;
        public uint   mBytesPerPacket;
        public uint   mFramesPerPacket;
        public uint   mBytesPerFrame;
        public uint   mChannelsPerFrame;
        public uint   mBitsPerChannel;
        public uint   mReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioTimeStamp
    {
        public double mSampleTime;
        public ulong  mHostTime;
        public double mRateScalar;
        public ulong  mWordClockTime;
        public double mSMPTETime_subframes;
        public double mSMPTETime_subframeDivisor;
        public uint   mSMPTETime_counter;
        public uint   mSMPTETime_type;
        public uint   mSMPTETime_flags;
        public uint   mSMPTETime_hours;
        public uint   mSMPTETime_minutes;
        public uint   mSMPTETime_seconds;
        public uint   mSMPTETime_frames;
        public uint   mFlags;
        public uint   mReserved;
    }

    [DllImport(AudioToolboxLib)]
    private static extern int AudioQueueNewInput(
        ref AudioStreamBasicDescription inFormat,
        AudioQueueInputCallback inCallbackProc,
        IntPtr inUserData,
        IntPtr inCallbackRunLoop,
        IntPtr inCallbackRunLoopMode,
        uint inFlags,
        out IntPtr outAQ);

    [DllImport(AudioToolboxLib)]
    private static extern int AudioQueueAllocateBuffer(
        IntPtr inAQ, uint inBufferByteSize, out IntPtr outBuffer);

    [DllImport(AudioToolboxLib)]
    private static extern int AudioQueueEnqueueBuffer(
        IntPtr inAQ, IntPtr inBuffer, uint inNumPacketDescs, IntPtr inPacketDescs);

    [DllImport(AudioToolboxLib)]
    private static extern int AudioQueueStart(IntPtr inAQ, IntPtr inStartTime);

    [DllImport(AudioToolboxLib)]
    private static extern int AudioQueueStop(IntPtr inAQ, bool inImmediate);

    [DllImport(AudioToolboxLib)]
    private static extern int AudioQueueDispose(IntPtr inAQ, bool inImmediate);

    // Offset of mAudioData inside AudioQueueBuffer opaque struct (Apple ABI)
    private const int BufferDataOffset = 8;
    private const int BufferByteSizeOffset = BufferDataOffset + 8;
    private const int NumBuffers = 3;
    private const uint BufferSize = 3200; // ~100 ms at 16kHz PCM-16

    // ── State ────────────────────────────────────────────────────────────────
    private IntPtr _queue = IntPtr.Zero;
    private readonly IntPtr[] _buffers = new IntPtr[NumBuffers];
    private List<byte> _capturedData = new();
    private readonly object _lock = new();

    // Keep delegate alive to prevent GC collection
    private AudioQueueInputCallback? _callbackDelegate;

    /// <summary>Smoothed per-bar amplitude levels [0, 1]. Read from any thread.</summary>
    public float[] WaveformLevels { get; } = new float[WaveformBars];

    public void Start()
    {
        lock (_lock)
        {
            _capturedData = new List<byte>(SampleRate * 2 * 30);
            Array.Clear(WaveformLevels, 0, WaveformBars);
        }

        var format = new AudioStreamBasicDescription
        {
            mSampleRate       = SampleRate,
            mFormatID         = 0x6C70636D, // 'lpcm'
            mFormatFlags      = 0x4,         // kLinearPCMFormatFlagIsSignedInteger
            mBytesPerPacket   = 2,
            mFramesPerPacket  = 1,
            mBytesPerFrame    = 2,
            mChannelsPerFrame = Channels,
            mBitsPerChannel   = BitDepth,
            mReserved         = 0
        };

        _callbackDelegate = AudioQueueCallback;

        int err = AudioQueueNewInput(
            ref format,
            _callbackDelegate,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            out _queue);

        if (err != 0)
            throw new InvalidOperationException($"AudioQueueNewInput failed: {err}");

        for (int i = 0; i < NumBuffers; i++)
        {
            AudioQueueAllocateBuffer(_queue, BufferSize, out _buffers[i]);
            AudioQueueEnqueueBuffer(_queue, _buffers[i], 0, IntPtr.Zero);
        }

        AudioQueueStart(_queue, IntPtr.Zero);
    }

    private void AudioQueueCallback(
        IntPtr inUserData,
        IntPtr inAQ,
        IntPtr inBuffer,
        ref AudioTimeStamp inStartTime,
        uint inNumberPacketDescriptions,
        IntPtr inPacketDescs)
    {
        // Read mAudioData pointer and mAudioDataByteSize from AudioQueueBuffer struct
        IntPtr dataPtr = Marshal.ReadIntPtr(inBuffer, BufferDataOffset);
        int    byteSize = Marshal.ReadInt32(inBuffer, BufferByteSizeOffset);

        if (byteSize > 0 && dataPtr != IntPtr.Zero)
        {
            byte[] chunk = new byte[byteSize];
            Marshal.Copy(dataPtr, chunk, 0, byteSize);

            lock (_lock)
                _capturedData.AddRange(chunk);

            UpdateWaveform(chunk, byteSize);
        }

        // Re-enqueue the buffer so recording continues
        if (_queue != IntPtr.Zero)
            AudioQueueEnqueueBuffer(inAQ, inBuffer, 0, IntPtr.Zero);
    }

    private void UpdateWaveform(byte[] chunk, int byteSize)
    {
        int sampleCount = byteSize / 2;
        if (sampleCount == 0) return;

        int samplesPerBar = Math.Max(1, sampleCount / WaveformBars);

        for (int bar = 0; bar < WaveformBars; bar++)
        {
            int start = bar * samplesPerBar;
            int end   = Math.Min(start + samplesPerBar, sampleCount);
            if (start >= end) break;

            double sum = 0;
            for (int i = start; i < end; i++)
            {
                float s = BitConverter.ToInt16(chunk, i * 2) / 32768f;
                sum += s * s;
            }

            float rms    = (float)Math.Sqrt(sum / (end - start));
            float target = Math.Min(1f, rms * 6f);
            float cur    = WaveformLevels[bar];
            WaveformLevels[bar] = target > cur ? target : cur * DecayCoef;
        }
    }

    public byte[] Stop()
    {
        if (_queue != IntPtr.Zero)
        {
            AudioQueueStop(_queue, true);
            AudioQueueDispose(_queue, true);
            _queue = IntPtr.Zero;
        }
        _callbackDelegate = null;

        lock (_lock)
            return _capturedData.ToArray();
    }

    // ── Static helpers (same API as Windows AudioRecorder) ────────────────────

    public static float[] ToFloatSamples(byte[] pcm)
    {
        float[] samples = new float[pcm.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
        return samples;
    }

    public static float[] TrimSilence(float[] samples, float threshold = 0.01f)
    {
        const int windowSize = 800;
        const int padding    = 3200;

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
        if (end <= start) return samples;

        var trimmed = new float[end - start];
        Array.Copy(samples, start, trimmed, 0, trimmed.Length);
        return trimmed;
    }

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
        if (_queue != IntPtr.Zero)
        {
            AudioQueueStop(_queue, true);
            AudioQueueDispose(_queue, true);
            _queue = IntPtr.Zero;
        }
        _callbackDelegate = null;
    }
}
