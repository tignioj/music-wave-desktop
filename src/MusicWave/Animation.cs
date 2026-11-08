using System.Diagnostics;
using MusicWave.Audio;

namespace MusicWave;

public readonly record struct MotionFrame(double Beats, double Strength, double Bpm, double Level, double Confidence);
public readonly record struct LayerMotion(double X, double Y, double ScaleX, double ScaleY);

public static class MotionMath
{
    public const double DesignWidth = 1920;
    public static double Pulse(double beats) => .5 - .5 * Math.Cos(2 * Math.PI * beats);
    // Coordinates and amplitude share the wallpaper's 1920-wide design canvas.
    // Both Explorer and the preview round in screen pixels before displaying them.
    public static int IconOffset(double x, double y, double screenWidth, MotionFrame frame, double amplitude, double spread)
    {
        var scale = screenWidth / DesignWidth;
        var phase = frame.Beats + (x / scale / 500 + y / scale / 1000) * spread;
        return (int)Math.Round(Pulse(phase) * amplitude * frame.Strength * scale);
    }
    public static LayerMotion ForLayer(AnimationSettings layer, MotionFrame frame)
    {
        if (layer.IsStatic) return new(0, 0, 1, 1);
        var phase = frame.Beats * layer.Speed + layer.Phase / 360;
        var pulse = Pulse(phase) * frame.Strength;
        return new(Math.Sin(phase * Math.PI * 2) * layer.Sway * frame.Strength,
            -layer.Bounce * pulse, 1 + layer.Squash * pulse * .24, Math.Max(.12, 1 - layer.Squash * pulse));
    }
    public static LayerMotion ForLayer(ProjectSettings project, WallpaperLayer layer, MotionFrame frame, double width, double height)
    {
        var group = LayerGroups.Find(project, layer);
        if (group is null) return ForLayer(layer, frame);
        var motion = ForLayer(group, frame);
        // Transform every member about one shared, persisted bottom anchor.
        return new(motion.X + (layer.X - group.AnchorX) / 100 * DesignWidth * (motion.ScaleX - 1),
            motion.Y + (layer.Y - group.AnchorY) / 100 * height / width * DesignWidth * (motion.ScaleY - 1), motion.ScaleX, motion.ScaleY);
    }
}

public sealed class RhythmClock
{
    readonly Stopwatch timer = Stopwatch.StartNew();
    double lastTime, beats, strength = 1, lastBeatAge = 100;
    public void Reset() { beats = 0; lastTime = timer.Elapsed.TotalSeconds; }
    public MotionFrame Tick(ProjectSettings settings, AudioSnapshot audio, bool running)
    {
        var now = timer.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - lastTime, 0, .1); lastTime = now;
        var bpm = settings.Automatic ? audio.Bpm : settings.Bpm;
        if (running)
        {
            beats += dt * bpm / 60 * settings.Multiplier;
            // Small phase correction, only after tempo has become reliable.
            if (settings.Automatic && audio.Confidence > .25 && audio.SinceBeat < lastBeatAge && settings.Multiplier == 1)
            {
                var error = .5 - (beats - Math.Floor(beats));
                if (Math.Abs(error) < .3) beats += error * .25;
            }
        }
        lastBeatAge = audio.SinceBeat;
        var target = !running ? 0 : settings.Automatic ? (audio.SinceAudio < .6 ? Math.Clamp(audio.Level * settings.Sensitivity * 14, .22, 1) : 0) : 1;
        strength += (target - strength) * (1 - Math.Exp(-dt * 9));
        return new(beats, strength * settings.Strength, bpm * settings.Multiplier, audio.SinceAudio < .6 ? audio.Level : 0, audio.Confidence);
    }
}
