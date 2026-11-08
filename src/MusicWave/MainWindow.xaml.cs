using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using MusicWave.Audio;
using MusicWave.Desktop;
using MusicWave.Rendering;

namespace MusicWave;

public partial class MainWindow : Window
{
    readonly AudioCapture audio = new();
    readonly DesktopIcons icons = new();
    readonly RhythmClock rhythm = new();
    readonly FramePump timer;
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Queue<double> taps = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    WallpaperWindow? wallpaper;
    TrayIcon? tray;
    ProjectSettings settings = null!;
    List<DesktopMonitor> monitors = [];
    bool applied, playing = true, audioStarted, closing, ready, dirty;
    double lastUi;
    int displayChanged;
    nint hwnd;
    public ProjectSettings Settings => settings;
    public MainWindow()
    {
        timer = new(Dispatcher, Tick);
        InitializeComponent();
        Preview.SelectedLayerChanged += (_, _) =>
        {
            if (Preview.SelectedLayer is null) LayerList.UnselectAll();
            else if (!LayerList.SelectedItems.Contains(Preview.SelectedLayer))
            {
                LayerList.UnselectAll(); LayerList.SelectedItem = Preview.SelectedLayer;
            }
        };
        try { settings = File.Exists(ProjectStore.DefaultPath) ? ProjectStore.Load(ProjectStore.DefaultPath) : ProjectSettings.Demo(); }
        catch (Exception ex) { settings = ProjectSettings.Demo(); Status("配置读取失败，已加载演示：" + ex.Message); }
        BindSettings();
        try { AudioDevices.ItemsSource = AudioCapture.Outputs(); }
        catch (Exception ex) { AudioDevices.ItemsSource = new List<AudioOutput> { new("", "系统默认输出") }; Status("音频设备不可用：" + ex.Message); }
        monitors = Native.Monitors(); Monitors.ItemsSource = monitors;
        if (settings.MonitorIndex >= monitors.Count) settings.MonitorIndex = 0;
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        saveTimer.Tick += (_, _) => { if (!dirty) return; try { ProjectStore.Save(settings, ProjectStore.DefaultPath); dirty = false; } catch (Exception ex) { Status("保存设置失败：" + ex.Message); } };
    }
    void BindSettings()
    {
        DataContext = settings;
        settings.PropertyChanged += SettingsChanged;
        settings.Layers.CollectionChanged += LayersChanged;
        settings.Groups.CollectionChanged += GroupsChanged;
        RebindGroups(); LayerGroups.RefreshNames(settings);
        foreach (var layer in settings.Layers) layer.PropertyChanged += LayerPropertyChanged;
        LayerList.SelectedIndex = settings.Layers.Count > 1 ? 1 : 0;
        RefreshGroupUi();
        dirty = true;
    }
    void UnbindSettings()
    {
        settings.PropertyChanged -= SettingsChanged; settings.Layers.CollectionChanged -= LayersChanged;
        foreach (var layer in settings.Layers) layer.PropertyChanged -= LayerPropertyChanged;
        settings.Groups.CollectionChanged -= GroupsChanged;
        foreach (var group in watchedGroups) group.PropertyChanged -= GroupPropertyChanged;
        watchedGroups.Clear();
    }
    void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        dirty = true;
        if (!ready) return;
        if (e.PropertyName is nameof(ProjectSettings.Automatic) or nameof(ProjectSettings.AudioDeviceId)) RestartAudio();
        if (applied && e.PropertyName is nameof(ProjectSettings.AnimateIcons) or nameof(ProjectSettings.AnimateWallpaper) or nameof(ProjectSettings.MonitorIndex)) ApplyDesktop();
        if (e.PropertyName == nameof(ProjectSettings.MonitorIndex)) RefreshIconPreview();
    }
    void LayersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (WallpaperLayer layer in e.OldItems) layer.PropertyChanged -= LayerPropertyChanged;
        if (e.NewItems is not null) foreach (WallpaperLayer layer in e.NewItems) layer.PropertyChanged += LayerPropertyChanged;
        LayerGroups.PruneEmpty(settings); RefreshGroupUi();
        dirty = true;
    }
    void LayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        dirty = true;
        if (e.PropertyName is nameof(WallpaperLayer.GroupId) or nameof(WallpaperLayer.Name)) RefreshGroupUi();
    }
    void OnLoaded(object sender, RoutedEventArgs e)
    {
        hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd).AddHook(WindowHook);
        var pauseRegistered = Native.RegisterHotKey(hwnd, 1, 0x4000 | 1 | 2, 0x50);
        var restoreRegistered = Native.RegisterHotKey(hwnd, 2, 0x4000 | 1 | 2, 0x52);
        tray = new(hwnd, ShowConsole);
        ready = true;
        if (File.Exists(DesktopIcons.RecoveryPath))
        {
            try { var count = DesktopIcons.RestorePending(); Status($"已恢复上次异常退出前的 {count} 个图标位置。"); }
            catch (Exception ex) { Error("图标恢复尚未完成", ex); }
        }
        RefreshIconPreview();
        RestartAudio(); rhythm.Reset(); timer.Start(); saveTimer.Start();
        if (!pauseRegistered || !restoreRegistered) Status(StatusText.Text + " · 部分快捷键被占用，可使用窗口按钮。");
    }
    nint WindowHook(nint handle, int message, nint wp, nint lp, ref bool handled)
    {
        if (message == 0x312) { if (wp == 1) TogglePause(); else if (wp == 2) StopDesktop(); handled = true; }
        if (message == 0x7E) Interlocked.Exchange(ref displayChanged, 1);
        if (message == 0x11) { StopDesktop(); } // WM_QUERYENDSESSION
        return 0;
    }
    void RestartAudio()
    {
        try
        {
            audio.Stop(); audioStarted = false;
            if (settings.Automatic) { audio.Start(settings.AudioDeviceId); audioStarted = true; Status("正在监听系统音频，约 4–12 秒后稳定节拍。"); }
            else Status("手动节拍模式 · 可调整 BPM、倍速或点按定速。");
        }
        catch (Exception ex) { Error("音频监听失败，可切换手动 BPM", ex); }
    }
    void Tick()
    {
        if (Interlocked.Exchange(ref displayChanged, 0) != 0)
        {
            StopDesktop(); monitors = Native.Monitors(); Monitors.ItemsSource = monitors; settings.MonitorIndex = 0;
            Status("显示器布局已改变，已停止并恢复图标；选择显示器后重新应用。");
        }
        var snapshot = audio.Snapshot();
        var frame = rhythm.Tick(settings, snapshot, playing);
        if (IsVisible && WindowState != WindowState.Minimized) Preview.Update(settings, frame);
        if (applied)
        {
            try
            {
                wallpaper?.Update(settings, frame);
                icons.Tick(frame, settings.IconAmplitude, settings.IconSpread);
            }
            catch (Exception ex) { StopDesktop(); Error("桌面连接中断，已停止效果", ex); }
        }
        if (clock.Elapsed.TotalSeconds - lastUi < .2) return;
        lastUi = clock.Elapsed.TotalSeconds;
        LiveBpm.Text = frame.Bpm.ToString("F0"); AudioMeter.Value = Math.Clamp(frame.Level * 10, 0, 1);
        AudioState.Text = !settings.Automatic ? $"手动节拍 · {frame.Bpm / 60:F2} Hz" : !audioStarted ? "音频未连接 · 请检查输出设备" : snapshot.Error is not null ? "设备已断开 · 请重新选择设备" : snapshot.SinceAudio >= .6 ? "等待音乐 · 静音时回到原位" : snapshot.Confidence < .2 ? "正在分析节拍…" : $"自动识别 {snapshot.Bpm:F0} BPM · 置信度 {snapshot.Confidence:P0}";
        if (Preview.ImageError is not null) Status(Preview.ImageError);
    }
    void ApplyDesktop()
    {
        if (!StopDesktop()) return;
        if (!settings.AnimateIcons && !settings.AnimateWallpaper) { Status("请至少启用桌面图标或动态壁纸。"); return; }
        try
        {
            Preview.Validate(settings);
            if (settings.AnimateWallpaper)
            {
                if (monitors.Count == 0) throw new InvalidOperationException("没有可用显示器");
                wallpaper = new(monitors[Math.Clamp(settings.MonitorIndex, 0, monitors.Count - 1)]);
                wallpaper.Attach(settings);
            }
            if (settings.AnimateIcons) icons.Start();
            RefreshIconPreview();
            applied = true; playing = true; PauseButton.Content = "暂停"; ApplyButton.Content = "✓  正在桌面运行";
            Status($"桌面效果已启动 · {icons.Count} 个真实图标 · 壁纸 {(wallpaper is null ? "关闭" : wallpaper.HostDescription)}");
        }
        catch (Exception ex) { StopDesktop(); Error("无法应用桌面效果", ex); }
    }
    public bool StopDesktop()
    {
        applied = false;
        wallpaper?.Close(); wallpaper = null;
        ApplyButton.Content = "▶  应用到桌面";
        try { icons.Stop(); Status("桌面效果已停止，图标已恢复。"); return true; }
        catch (Exception ex) { Error("图标恢复未完成；可点击“恢复图标位置”重试", ex); return false; }
    }
    void RefreshIconPreview()
    {
        if (monitors.Count == 0) return;
        var monitor = monitors[Math.Clamp(settings.MonitorIndex, 0, monitors.Count - 1)];
        Preview.Height = Preview.Width * monitor.Bounds.Height / monitor.Bounds.Width;
        try { Preview.PreviewIcons = icons.PreviewLayout(monitor); }
        catch (Exception ex) { Preview.PreviewIcons = null; App.Log("读取图标预览布局", ex); }
    }
    void TogglePause() { playing = !playing; PauseButton.Content = playing ? "暂停" : "继续"; Status(playing ? "动画已继续。" : "动画已暂停，图标与图层回到基准位置。"); }
    void Status(string text) => StatusText.Text = text;
    void Error(string title, Exception ex) { Status(title + "：" + ex.Message); App.Log(title, ex); }
    void ShowConsole() { Show(); WindowState = WindowState.Normal; Activate(); }
    void Apply_Click(object sender, RoutedEventArgs e) => ApplyDesktop();
    void Stop_Click(object sender, RoutedEventArgs e) => StopDesktop();
    void Pause_Click(object sender, RoutedEventArgs e) => TogglePause();
    void Hide_Click(object sender, RoutedEventArgs e) { Hide(); Status("仍在后台运行，双击托盘图标打开。"); }
    void Restore_Click(object sender, RoutedEventArgs e) { if (StopDesktop()) { try { var count = DesktopIcons.RestorePending(); Status($"图标位置已恢复{(count > 0 ? $"（{count} 个）" : "")}。"); } catch (Exception ex) { Error("恢复失败", ex); } } }
    void Speed_Click(object sender, RoutedEventArgs e) => settings.Multiplier = double.Parse(((Button)sender).Tag.ToString()!, CultureInfo.InvariantCulture);
    void Tap_Click(object sender, RoutedEventArgs e)
    {
        var now = clock.Elapsed.TotalSeconds;
        if (taps.Count > 0 && now - taps.Last() > 2) taps.Clear();
        taps.Enqueue(now); while (taps.Count > 8) taps.Dequeue();
        if (taps.Count >= 2) { settings.Automatic = false; settings.Bpm = 60 * (taps.Count - 1) / (taps.Last() - taps.First()); rhythm.Reset(); Status($"点按定速：{settings.Bpm:F0} BPM"); }
        else Status("继续跟着节奏点按…");
    }
    void Layer_Changed(object sender, SelectionChangedEventArgs e)
    {
        var layer = e.AddedItems.OfType<WallpaperLayer>().LastOrDefault() ?? LayerList.SelectedItem as WallpaperLayer;
        if (LayerEditor is not null) { LayerEditor.DataContext = layer; LayerEditor.IsEnabled = layer is not null; }
        if (Preview is not null) Preview.SelectedLayer = layer;
        if (settings is not null && GroupPicker is not null && GroupPicker.SelectedItem is null && layer is not null && LayerGroups.Find(settings, layer) is { } group) GroupPicker.SelectedItem = group;
        RefreshGroupUi();
    }
    void AudioDevice_Changed(object sender, SelectionChangedEventArgs e) { }
    void Monitor_Changed(object sender, SelectionChangedEventArgs e) { }
    static string? PickImage(Window owner)
    {
        var dialog = new OpenFileDialog { Title = "选择图片（透明 PNG 适合独立动态图层）", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|所有文件|*.*", CheckFileExists = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
    void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        var path = PickImage(this); if (path is null) return;
        try { SceneRenderer.LoadImage(path); var layer = new WallpaperLayer { Name = Path.GetFileNameWithoutExtension(path), Path = path }; settings.Layers.Add(layer); LayerList.SelectedItem = layer; }
        catch (Exception ex) { Error("图片无法读取", ex); }
    }
    static readonly HashSet<string> imageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };
    internal static string[] DroppedImages(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths
        ? paths.Where(p => imageExtensions.Contains(Path.GetExtension(p)) && File.Exists(p)).ToArray() : [];
    void Images_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DroppedImages(e.Data).Length > 0 ? DragDropEffects.Copy & e.AllowedEffects : DragDropEffects.None;
        e.Handled = true;
    }
    void Images_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        if (!e.AllowedEffects.HasFlag(DragDropEffects.Copy)) { e.Effects = DragDropEffects.None; return; }
        var paths = DroppedImages(e.Data);
        if (paths.Length == 0) { Status("请拖入 PNG、JPEG、BMP 或 TIFF 图片文件。"); return; }
        var position = e.GetPosition(Preview);
        var inPreview = new Rect(Preview.RenderSize).Contains(position) && Preview.ActualWidth > 0 && Preview.ActualHeight > 0;
        var x = inPreview ? position.X / Preview.ActualWidth * 100 : 50;
        var y = inPreview ? position.Y / Preview.ActualHeight * 100 : 82;
        var (added, lastLayer, errors) = ImportImages(settings, paths, x, y);
        if (lastLayer is not null) LayerList.SelectedItem = lastLayer;
        e.Effects = added > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        Status($"已拖入 {added} 个动态图层。" + (errors.Count > 0 ? $" {errors.Count} 张图片读取失败：{string.Join("；", errors)}" : "可在预览中拖动位置或拉伸尺寸。"));
    }
    internal static (int Count, WallpaperLayer? LastLayer, List<string> Errors) ImportImages(ProjectSettings target, IEnumerable<string> paths, double x, double y)
    {
        var added = 0; WallpaperLayer? lastLayer = null; var errors = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                SceneRenderer.LoadImage(path);
                var layer = new WallpaperLayer { Name = Path.GetFileNameWithoutExtension(path), Path = path, X = x + added * 2, Y = y + added * 2 };
                target.Layers.Add(layer); lastLayer = layer; added++;
            }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(path)}：{ex.Message}"); }
        }
        return (added, lastLayer, errors);
    }
    void Background_Click(object sender, RoutedEventArgs e)
    {
        var path = PickImage(this); if (path is null) return;
        try
        {
            SceneRenderer.LoadImage(path);
            var layer = new WallpaperLayer { Name = "背景 · " + Path.GetFileNameWithoutExtension(path), Path = path, IsStatic = true, Width = 100, Height = 100, X = 50, Y = 100 };
            if (settings.Layers.Count > 0 && settings.Layers[0].IsStatic) settings.Layers[0] = layer; else settings.Layers.Insert(0, layer);
            LayerList.SelectedItem = layer;
        }
        catch (Exception ex) { Error("背景无法读取", ex); }
    }
    void Replace_Click(object sender, RoutedEventArgs e)
    {
        if (LayerEditor.DataContext is not WallpaperLayer layer) return;
        var path = PickImage(this); if (path is null) return;
        try { SceneRenderer.LoadImage(path); layer.Path = path; Preview.ClearCache(); wallpaper?.InvalidateImages(); }
        catch (Exception ex) { Error("图片无法读取", ex); }
    }
    void Up_Click(object sender, RoutedEventArgs e) { var i = LayerList.SelectedIndex; if (i > 0) settings.Layers.Move(i, i - 1); }
    void Down_Click(object sender, RoutedEventArgs e) { var i = LayerList.SelectedIndex; if (i >= 0 && i + 1 < settings.Layers.Count) settings.Layers.Move(i, i + 1); }
    void Remove_Click(object sender, RoutedEventArgs e) { foreach (var layer in SelectedLayers()) settings.Layers.Remove(layer); }
    void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "导出项目（图片路径保存在项目中）", Filter = "MusicWave 项目|*.musicwave.json", FileName = "我的律动桌面.musicwave.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { ProjectStore.Save(settings, dialog.FileName); Status("项目已导出：" + dialog.FileName); }
        catch (Exception ex) { Error("项目保存失败", ex); }
    }
    void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "打开 MusicWave 项目", Filter = "MusicWave 项目|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var loaded = ProjectStore.Load(dialog.FileName); Preview.ClearCache(); Preview.Validate(loaded);
            if (!StopDesktop()) return;
            audio.Stop(); UnbindSettings(); settings = loaded; BindSettings(); RestartAudio(); rhythm.Reset();
            RefreshIconPreview();
            Status("项目已加载，点击“应用到桌面”启用。");
        }
        catch (Exception ex) { Error("项目打开失败", ex); }
    }
    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return; closing = true;
        timer.Dispose(); saveTimer.Stop(); StopDesktop(); audio.Dispose(); tray?.Dispose();
        Native.UnregisterHotKey(hwnd, 1); Native.UnregisterHotKey(hwnd, 2);
        try { ProjectStore.Save(settings, ProjectStore.DefaultPath); } catch (Exception ex) { App.Log("退出时保存配置", ex); }
    }
}
