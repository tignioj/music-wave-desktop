using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MusicWave.Audio;

public sealed record AudioOutput(string Id, string Name) { public override string ToString() => Name; }
public sealed record AudioSnapshot(double Bpm, double Confidence, double Level, double SinceAudio, double SinceBeat, string? Error);

public sealed class AudioCapture : IDisposable
{
    readonly object gate = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    TempoDetector detector = new();
    WasapiLoopbackCapture? capture;
    MMDevice? device;
    MMDeviceEnumerator? enumerator;
    double lastAudio = -100, lastBeat = -100, low, sum, fullSum, previousRms, averageRms;
    int frames;
    string? error;
    bool stopping;
    public static List<AudioOutput> Outputs()
    {
        using var e = new MMDeviceEnumerator();
        var outputs = new List<AudioOutput> { new("", "系统默认输出") };
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        { using (d) outputs.Add(new(d.ID, d.FriendlyName)); }
        return outputs;
    }
    public void Start(string deviceId)
    {
        Stop();
        lock (gate) { detector = new(); lastAudio = lastBeat = -100; low = sum = fullSum = previousRms = averageRms = 0; frames = 0; error = null; stopping = false; }
        try
        {
            enumerator = new();
            device = string.IsNullOrEmpty(deviceId) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(deviceId);
            capture = new(device);
            capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(device.AudioClient.MixFormat.SampleRate, device.AudioClient.MixFormat.Channels);
            capture.DataAvailable += OnAudio;
            capture.RecordingStopped += OnStopped;
            capture.StartRecording();
        }
        catch { Stop(); throw; }
    }
    void OnStopped(object? sender, StoppedEventArgs e)
    { lock (gate) { if (!stopping) error = e.Exception?.Message ?? "音频设备已停止，请重新开始监听。"; } }
    void OnAudio(object? sender, WaveInEventArgs e)
    {
        lock (gate)
        {
            if (stopping || capture is null) return;
            var channels = capture.WaveFormat.Channels;
            var sampleRate = capture.WaveFormat.SampleRate;
            var hop = Math.Max(1, sampleRate / 100);
            var alpha = 1 - Math.Exp(-2 * Math.PI * 180 / sampleRate);
            for (var offset = 0; offset + channels * 4 <= e.BytesRecorded; offset += channels * 4)
            {
                double sample = 0, channelEnergy = 0;
                for (var c = 0; c < channels; c++) { var v = BitConverter.ToSingle(e.Buffer, offset + c * 4); sample += v / channels; channelEnergy += v * v / channels; }
                low += alpha * (sample - low);
                sum += low * low;
                fullSum += channelEnergy;
                if (++frames < hop) continue;
                var rms = Math.Sqrt(sum / frames);
                var fullRms = Math.Sqrt(fullSum / frames);
                detector.PushEnergy(rms * .8 + fullRms * .2);
                if (fullRms > .0002) lastAudio = clock.Elapsed.TotalSeconds;
                averageRms = averageRms * .96 + rms * .04;
                var now = clock.Elapsed.TotalSeconds;
                if (rms > Math.Max(.003, averageRms * 1.3) && rms > previousRms * 1.15 && now - lastBeat > .22) lastBeat = now;
                previousRms = rms;
                sum = fullSum = 0; frames = 0;
            }
        }
    }
    public AudioSnapshot Snapshot()
    {
        lock (gate) { var now = clock.Elapsed.TotalSeconds; return new(detector.Bpm, detector.Confidence, detector.Level, now - lastAudio, now - lastBeat, error); }
    }
    public void Stop()
    {
        WasapiLoopbackCapture? old;
        lock (gate) { stopping = true; old = capture; capture = null; }
        if (old is not null) { old.DataAvailable -= OnAudio; old.RecordingStopped -= OnStopped; old.StopRecording(); old.Dispose(); }
        device?.Dispose(); device = null; enumerator?.Dispose(); enumerator = null;
    }
    public void Dispose() => Stop();
}
