using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MusicWave;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; PropertyChanged?.Invoke(this, new(name)); } }
}

public sealed class WallpaperLayer : Observable
{
    string name = "新图层", path = "";
    bool isStatic, visible = true;
    double x = 50, y = 82, width = 40, height = 60, opacity = 1, bounce = 28, squash = .34, sway, phase, speed = 1;
    public string Name { get => name; set => Set(ref name, value); }
    public string Path { get => path; set => Set(ref path, value); }
    public bool IsStatic { get => isStatic; set => Set(ref isStatic, value); }
    public bool Visible { get => visible; set => Set(ref visible, value); }
    // X = horizontal center; Y = bottom anchor; all layout values are percentages of the screen.
    public double X { get => x; set => Set(ref x, Math.Clamp(value, -100, 200)); }
    public double Y { get => y; set => Set(ref y, Math.Clamp(value, -100, 200)); }
    public double Width { get => width; set => Set(ref width, Math.Clamp(value, 1, 300)); }
    public double Height { get => height; set => Set(ref height, Math.Clamp(value, 1, 300)); }
    public double Opacity { get => opacity; set => Set(ref opacity, Math.Clamp(value, 0, 1)); }
    public double Bounce { get => bounce; set => Set(ref bounce, Math.Clamp(value, 0, 300)); }
    public double Squash { get => squash; set => Set(ref squash, Math.Clamp(value, 0, .85)); }
    public double Sway { get => sway; set => Set(ref sway, Math.Clamp(value, 0, 200)); }
    public double Phase { get => phase; set => Set(ref phase, Math.Clamp(value, 0, 360)); }
    public double Speed { get => speed; set => Set(ref speed, Math.Clamp(value, .25, 4)); }
}

public sealed class ProjectSettings : Observable
{
    double bpm = 120, multiplier = 1, iconAmplitude = 26, iconSpread = .45, sensitivity = 1, strength = 1;
    bool automatic, icons = true, wallpaper = true;
    int monitorIndex;
    string audioDeviceId = "";
    public double Bpm { get => bpm; set => Set(ref bpm, Math.Clamp(value, 30, 300)); }
    public double Multiplier { get => multiplier; set => Set(ref multiplier, Math.Clamp(value, .25, 4)); }
    public double IconAmplitude { get => iconAmplitude; set => Set(ref iconAmplitude, Math.Clamp(value, 0, 160)); }
    public double IconSpread { get => iconSpread; set => Set(ref iconSpread, Math.Clamp(value, 0, 2)); }
    public double Sensitivity { get => sensitivity; set => Set(ref sensitivity, Math.Clamp(value, .2, 4)); }
    public double Strength { get => strength; set => Set(ref strength, Math.Clamp(value, 0, 2)); }
    public bool Automatic { get => automatic; set => Set(ref automatic, value); }
    public bool AnimateIcons { get => icons; set => Set(ref icons, value); }
    public bool AnimateWallpaper { get => wallpaper; set => Set(ref wallpaper, value); }
    public int MonitorIndex { get => monitorIndex; set => Set(ref monitorIndex, Math.Max(0, value)); }
    public string AudioDeviceId { get => audioDeviceId; set => Set(ref audioDeviceId, value); }
    public ObservableCollection<WallpaperLayer> Layers { get; set; } = [];
    public static ProjectSettings Demo() => new()
    {
        Layers = [
            new() { Name = "夜色 · 静态背景", Path = "demo:sky", IsStatic = true, X = 50, Y = 100, Width = 100, Height = 100 },
            new() { Name = "月光 · 主动态图层", Path = "demo:orb", X = 52, Y = 79, Width = 40, Height = 68, Bounce = 22, Squash = .38 },
            new() { Name = "青色星体 · 动态", Path = "demo:mint", X = 23, Y = 84, Width = 14, Height = 24, Phase = 55, Bounce = 35, Squash = .3 },
            new() { Name = "紫色星体 · 动态", Path = "demo:violet", X = 79, Y = 87, Width = 18, Height = 31, Phase = 110, Bounce = 20, Squash = .42 }
        ]
    };
}

public static class ProjectStore
{
    public static readonly string DataDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicWave");
    public static string DefaultPath => System.IO.Path.Combine(DataDirectory, "settings.json");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static ProjectSettings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<ProjectSettings>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("项目为空");
        if (settings.Layers is null || settings.Layers.Any(l => l is null)) throw new InvalidDataException("项目图层无效");
        foreach (var layer in settings.Layers)
        {
            if (string.IsNullOrWhiteSpace(layer.Path)) throw new InvalidDataException("图层缺少图片路径");
            if (!layer.Path.StartsWith("demo:") && !System.IO.Path.IsPathRooted(layer.Path))
                layer.Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, layer.Path));
        }
        return settings;
    }
    public static void Save(ProjectSettings settings, string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings, Json));
        File.Move(path + ".tmp", path, true);
    }
}
