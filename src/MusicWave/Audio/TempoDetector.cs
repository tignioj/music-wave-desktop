namespace MusicWave.Audio;

// 100 Hz positive energy-flux envelope; normalized autocorrelation over a rolling 12 s window.
// No microphone, audio recording, network call, or external player is needed.
public sealed class TempoDetector
{
    const int EnvelopeRate = 100, Capacity = 1200;
    readonly double[] flux = new double[Capacity];
    int cursor, count, untilAnalysis;
    double previousEnergy;
    public double Bpm { get; private set; } = 120;
    public double Confidence { get; private set; }
    public double Level { get; private set; }
    public void PushEnergy(double rms)
    {
        Level = rms;
        flux[cursor] = Math.Max(0, rms - previousEnergy * .97);
        previousEnergy = previousEnergy * .4 + rms * .6;
        cursor = (cursor + 1) % Capacity;
        count = Math.Min(count + 1, Capacity);
        if (++untilAnalysis >= 50 && count >= 350) { untilAnalysis = 0; Analyze(); }
    }
    void Analyze()
    {
        var signal = new double[count];
        for (var i = 0; i < count; i++) signal[i] = flux[(cursor - count + Capacity + i) % Capacity];
        var mean = signal.Average();
        for (var i = 0; i < count; i++) signal[i] = Math.Max(0, signal[i] - mean * .7);
        if (signal.Sum(v => v * v) < .0000001) { Confidence = 0; return; }
        // Spread each onset over ±20 ms. Without this, a non-integer period such as
        // 180 BPM (33⅓ samples) can correlate more strongly at its exact 100-sample
        // three-beat harmonic and be misreported as 60 BPM.
        var smoothed = new double[count];
        for (var i = 2; i < count - 2; i++)
            smoothed[i] = (signal[i - 2] + 2 * signal[i - 1] + 3 * signal[i] + 2 * signal[i + 1] + signal[i + 2]) / 9;
        signal = smoothed;
        double best = 0, bestLag = 50;
        var correlations = new double[101];
        for (var lag = 25; lag <= 100; lag++)
        {
            double cross = 0, a = 0, b = 0;
            for (var i = lag; i < count; i++) { cross += signal[i] * signal[i - lag]; a += signal[i] * signal[i]; b += signal[i - lag] * signal[i - lag]; }
            correlations[lag] = cross / Math.Sqrt(Math.Max(1e-20, a * b));
        }
        for (var lag = 25; lag <= 100; lag++)
        {
            // Prefer the fundamental over a two-beat period, while retaining genuine slow tempos.
            double score = correlations[lag] * (.94 + .06 * (100 - lag) / 75.0);
            if (lag * 2 <= 100) score += .12 * correlations[lag * 2];
            if (lag % 2 == 0 && lag / 2 >= 25 && correlations[lag / 2] > correlations[lag] * .8) score *= .82;
            if (score > best) { best = score; bestLag = lag; }
        }
        Confidence = Math.Clamp(best, 0, 1);
        if (Confidence < .12) return;
        var index = (int)bestLag;
        if (index > 25 && index < 100)
        {
            var denominator = correlations[index - 1] - 2 * correlations[index] + correlations[index + 1];
            if (Math.Abs(denominator) > 1e-9) bestLag += Math.Clamp(.5 * (correlations[index - 1] - correlations[index + 1]) / denominator, -.5, .5);
        }
        var measured = 60.0 * EnvelopeRate / bestLag;
        Bpm = Math.Abs(measured - Bpm) > 8 ? measured : Bpm * .65 + measured * .35;
    }
}
