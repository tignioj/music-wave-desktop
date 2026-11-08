using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicWave.Audio;
using MusicWave.Desktop;
using MusicWave.Rendering;

namespace MusicWave;

public static class Diagnostics
{
    public static void Run(App app, string[] args)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppContext.BaseDirectory, "diagnostics.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var report = new StringBuilder();
        try
        {
            switch (args[0])
            {
                case "--wallpaper-check": report.AppendLine(Native.DescribeDesktop()); break;
                case "--self-test": SelfTest(report, Path.GetDirectoryName(output)!); break;
                case "--desktop-check":
                    var icons = DesktopIcons.Inspect();
                    report.AppendLine($"Shell connected: {icons.Icons.Count} icons, flags=0x{icons.Flags:X}");
                    report.AppendLine($"Monitors: {string.Join("; ", Native.Monitors())}");
                    report.AppendLine($"Outputs: {string.Join("; ", AudioCapture.Outputs())}");
                    break;
                case "--desktop-smoke": DesktopSmoke(app, output, report); return;
                case "--animation-benchmark": AnimationBenchmark(app, output, report); return;
                case "--icon-observe": ObserveIcons(app, output, report); return;
                case "--icon-motion-check": CheckIconMotion(app, output, report); return;
                case "--icon-visual-observe": IconVisualDiagnostics.Observe(app, output, report); return;
                case "--icon-visual-check": IconVisualDiagnostics.Check(app, output, report); return;
                case "--audio-smoke": AudioSmoke(app, output, report); return;
                default: throw new ArgumentException("支持 --self-test、--desktop-check、--desktop-smoke、--animation-benchmark、--icon-observe、--icon-motion-check、--icon-visual-observe、--icon-visual-check、--audio-smoke、--wallpaper-check");
            }
            report.AppendLine("PASS"); File.WriteAllText(output, report.ToString()); app.Shutdown(0);
        }
        catch (Exception ex) { report.AppendLine("FAIL: " + ex); File.WriteAllText(output, report.ToString()); app.Shutdown(1); }
    }
    static void Assert(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
    // Read-only measurement of Explorer's actual positions while the main app animates.
    static void ObserveIcons(App app, string output, StringBuilder report)
    {
        var samples = new Dictionary<string, List<int>>();
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            try
            {
                foreach (var icon in DesktopIcons.Inspect().Icons)
                {
                    if (!samples.TryGetValue(icon.Identity, out var values)) samples[icon.Identity] = values = [];
                    values.Add(icon.Y);
                }
                if (watch.Elapsed.TotalSeconds < 3) return;
                timer.Stop();
                foreach (var (values, index) in samples.Values.Select((v, i) => (v, i)))
                    report.AppendLine($"Icon {index + 1}: n={values.Count}, minY={values.Min()}, maxY={values.Max()}, travel={values.Max() - values.Min()} Shell px, distinct={values.Distinct().Count()}");
                report.AppendLine("PASS: read-only observation; no desktop positions or settings changed.");
                File.WriteAllText(output, report.ToString()); app.Shutdown(0);
            }
            catch (Exception ex) { timer.Stop(); report.AppendLine("FAIL: " + ex); File.WriteAllText(output, report.ToString()); app.Shutdown(1); }
        };
        timer.Start();
    }
    static void CheckIconMotion(App app, string output, StringBuilder report)
    {
        var controller = new DesktopIcons();
        var before = DesktopIcons.Inspect();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
        try
        {
            Assert(!File.Exists(DesktopIcons.RecoveryPath), "Stop MusicWave before checking icon motion.");
            Assert(before.Icons.Count > 0, "No desktop icons available to measure.");
            var origin = ShellConnection.ScreenOrigin();
            var monitors = Native.Monitors();
            var poses = new List<(double Beat, double Amplitude, double Spread, double Strength)>();
            foreach (var spread in new[] { 0.0, .45 })
                foreach (var amplitude in new[] { 26.0, 72 })
                    for (var i = 0; i <= 8; i++) poses.Add((i / 8.0, amplitude, spread, 1));
            poses.Add((.5, 26, 0, .35)); poses.Add((.5, 26, 0, 0));
            controller.Start();
            var previews = monitors.Select(controller.PreviewLayout).ToArray();
            var step = 0;
            timer.Tick += (_, _) =>
            {
                try
                {
                    var pose = poses[step];
                    controller.Tick(new(pose.Beat, pose.Strength, 120, 0, 1), pose.Amplitude, pose.Spread);
                    var actual = DesktopIcons.Inspect().Icons.ToDictionary(i => i.Identity);
                    foreach (var original in before.Icons)
                    {
                        var screenX = original.X + origin.X; var screenY = original.Y + origin.Y;
                        var monitor = monitors.FirstOrDefault(m => screenX >= m.Bounds.Left && screenX < m.Bounds.Right && screenY >= m.Bounds.Top && screenY < m.Bounds.Bottom) ?? monitors[0];
                        var bounds = monitor.Bounds;
                        var scale = bounds.Width / 1920.0;
                        var localX = screenX - bounds.Left; var localY = screenY - bounds.Top;
                        var phase = pose.Beat + (localX / scale / 500 + localY / scale / 1000) * pose.Spread;
                        var expectedOffset = (int)Math.Round((1 - Math.Cos(2 * Math.PI * phase)) / 2 * pose.Amplitude * pose.Strength * scale);
                        Assert(actual.TryGetValue(original.Identity, out var observed) && observed.X == original.X && observed.Y - original.Y == expectedOffset,
                            $"Explorer displacement differs from preview: beat={pose.Beat}, amplitude={pose.Amplitude}, spread={pose.Spread}, expected={expectedOffset}, actual={observed?.Y - original.Y}");
                        Assert(previews.Any(p => p.Bounds.Left == bounds.Left && p.Bounds.Top == bounds.Top && p.Positions.Any(i => i.X == localX && i.Y == localY)), "Preview did not retain the original icon anchor.");
                    }
                    if (pose.Beat == .5 && pose.Spread == 0)
                        report.AppendLine($"Peak: amplitude={pose.Amplitude}, strength={pose.Strength:F2}; actual travel={string.Join(", ", before.Icons.Select(i => actual[i.Identity].Y - i.Y).Distinct())} Shell px.");
                    if (++step < poses.Count) return;
                    timer.Stop(); controller.Stop();
                    var after = DesktopIcons.Inspect();
                    Assert(after.Flags == before.Flags && after.Icons.SequenceEqual(before.Icons), "Desktop did not restore exactly.");
                    report.AppendLine($"PASS: {poses.Count} poses × {before.Icons.Count} real icons; scaled amplitude, spatial phase, reduced strength, silence, preview anchors and exact restoration.");
                    File.WriteAllText(output, report.ToString()); app.Shutdown(0);
                }
                catch (Exception ex) { Fail(ex); }
            };
            timer.Start();
        }
        catch (Exception ex) { Fail(ex); }
        void Fail(Exception ex)
        {
            timer.Stop();
            try { controller.Stop(); } catch (Exception restore) { report.AppendLine(restore.ToString()); }
            report.AppendLine("FAIL: " + ex); File.WriteAllText(output, report.ToString()); app.Shutdown(1);
        }
    }
    static void AnimationBenchmark(App app, string output, StringBuilder report)
    {
        var controller = new DesktopIcons();
        WallpaperWindow? wallpaper = null;
        var before = DesktopIcons.Inspect();
        try
        {
            Assert(!File.Exists(DesktopIcons.RecoveryPath), "Stop MusicWave before benchmarking.");
            var project = ProjectSettings.Demo();
            var preview = new SceneRenderer { Width = 960, Height = 540, ShowPreviewIcons = true };
            var console = new Window { Title = "MusicWave 动画性能检测", Width = 960, Height = 540, Content = preview };
            console.Show();
            controller.Start();
            preview.PreviewIcons = controller.PreviewLayout(Native.Monitors()[0]);
            wallpaper = new(Native.Monitors()[0]); wallpaper.Attach(project);
            var watch = Stopwatch.StartNew();
            var intervals = new List<double>(); var visibleIntervals = new List<double>(); var hiddenIntervals = new List<double>();
            var wallpaperMs = new List<double>(); var iconMs = new List<double>();
            double last = 0;
            var iconCount = controller.Count;
            var uploads = wallpaper.UploadCount;
            FramePump timer = null!;
            timer = new(app.Dispatcher, () =>
            {
                try
                {
                    var now = watch.Elapsed.TotalSeconds;
                    if (now > 1)
                    {
                        var interval = (now - last) * 1000; intervals.Add(interval);
                        if (now < 5) visibleIntervals.Add(interval); else if (now > 5.2) hiddenIntervals.Add(interval);
                    }
                    last = now;
                    var frame = new MotionFrame(now * 2, 1, 120, 0, 1);
                    if (now < 5) preview.Update(project, frame); else if (console.IsVisible) console.Hide();
                    var start = Stopwatch.GetTimestamp();
                    wallpaper.Update(project, frame);
                    if (now > 1) wallpaperMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    start = Stopwatch.GetTimestamp(); controller.Tick(frame, 26, .45);
                    if (now > 1) iconMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    if (now < 9) return;
                    timer.Dispose();
                    Assert(wallpaper.UploadCount == uploads, "Animation re-uploaded unchanged image pixels.");
                    var writes = controller.PositionWrites;
                    var rest = new MotionFrame(0, 0, 120, 0, 0);
                    wallpaper.Update(project, rest); controller.Tick(rest, 26, .45);
                    var pausedCommits = wallpaper.CommitCount; var pausedWrites = controller.PositionWrites;
                    for (var i = 0; i < 60; i++) { wallpaper.Update(project, rest); controller.Tick(rest, 26, .45); }
                    Assert(wallpaper.CommitCount == pausedCommits && controller.PositionWrites == pausedWrites, "Paused animation keeps updating identical content.");
                    wallpaper.Dispose(); controller.Stop();
                    var after = DesktopIcons.Inspect();
                    Assert(after == before || (after.Flags == before.Flags && after.Icons.SequenceEqual(before.Icons)), "Benchmark did not restore desktop exactly.");
                    void Stats(string name, List<double> values)
                    {
                        values.Sort();
                        report.AppendLine($"{name}: n={values.Count}, mean={values.Average():F3}ms, p95={values[(int)((values.Count - 1) * .95)]:F3}ms, max={values[^1]:F3}ms");
                    }
                    report.AppendLine($"Monitor: {wallpaper.Monitor}; icons: {iconCount}; measured after 1s warmup.");
                    Stats("Frame interval", intervals); Stats("Wallpaper update", wallpaperMs); Stats("Shell update", iconMs);
                    Stats("Preview visible frame interval", visibleIntervals); Stats("Console hidden frame interval", hiddenIntervals);
                    report.AppendLine($"Wallpaper FPS: {1000 / intervals.Average():F2}; Shell calls/sec: {iconMs.Count / (now - 1):F2}");
                    report.AppendLine($"GPU uploads: {uploads} at initialization, zero while animating; Shell writes: {writes} including warmup.");
                    report.AppendLine("PASS: paused frames perform no GPU commits or Shell writes.");
                    report.AppendLine("PASS: desktop restored exactly.");
                    console.Close();
                    File.WriteAllText(output, report.ToString()); app.Shutdown(0);
                }
                catch (Exception ex)
                {
                    timer.Dispose(); wallpaper.Dispose(); try { controller.Stop(); } catch (Exception restore) { report.AppendLine(restore.ToString()); }
                    console.Close();
                    report.AppendLine("FAIL: " + ex); File.WriteAllText(output, report.ToString()); app.Shutdown(1);
                }
            });
            timer.Start();
        }
        catch (Exception ex)
        {
            wallpaper?.Dispose(); controller.Stop(); report.AppendLine("FAIL: " + ex); File.WriteAllText(output, report.ToString()); app.Shutdown(1);
        }
    }
    static void SelfTest(StringBuilder report, string directory)
    {
        foreach (var bpm in new[] { 72.0, 90, 120, 150, 180 })
        {
            var detector = new TempoDetector();
            var random = new Random(7);
            for (var i = 0; i < 1800; i++)
            {
                var phase = i / 100.0 * bpm / 60;
                var envelope = .2 * Math.Exp(-((phase % 1) * 60 / bpm) * 30) + random.NextDouble() * .001;
                detector.PushEnergy(envelope);
            }
            Assert(Math.Abs(detector.Bpm - bpm) < 3, $"Tempo {bpm}: detected {detector.Bpm:F2}");
            report.AppendLine($"Tempo {bpm:F0} BPM => {detector.Bpm:F2}, confidence {detector.Confidence:F2}");
        }
        var silent = new TempoDetector(); for (var i = 0; i < 1500; i++) silent.PushEnergy(0);
        Assert(silent.Confidence == 0, "Silence must not invent a tempo");
        var layer = new WallpaperLayer { IsStatic = true, Bounce = 70, Squash = .8, Sway = 50 };
        Assert(MotionMath.ForLayer(layer, new(.5, 1, 120, 0, 1)) == new LayerMotion(0, 0, 1, 1), "Static layer moved");
        layer.IsStatic = false;
        var rest = MotionMath.ForLayer(layer, new(0, 1, 120, 0, 1));
        var peak = MotionMath.ForLayer(layer, new(.5, 1, 120, 0, 1));
        Assert(rest.ScaleY == 1 && peak.ScaleY < .3 && peak.Y == -70, "Bottom-anchored squash/bounce failed");
        Assert(MotionMath.ForLayer(layer, new(.5, 0, 120, 0, 1)) == new LayerMotion(0, 0, 1, 1), "Silence strength failed");
        Assert(MotionMath.IconOffset(0, 0, 1920, new(.5, 1, 120, 0, 1), 26, 0) == 26, "1080p icon amplitude failed");
        Assert(MotionMath.IconOffset(0, 0, 2560, new(.5, 1, 120, 0, 1), 26, 0) == 35, "1440p icon amplitude failed");
        Assert(MotionMath.IconOffset(0, 0, 3840, new(.5, 1, 120, 0, 1), 26, 0) == 52, "4K icon amplitude failed");
        Assert(MotionMath.IconOffset(200, 100, 1920, new(0, 1, 120, 0, 1), 26, 1) == 26 &&
            MotionMath.IconOffset(400, 200, 3840, new(0, 1, 120, 0, 1), 26, 1) == 52, "Resolution changed spatial phase");
        Assert(MotionMath.IconOffset(400, 200, 3840, new(.5, 0, 120, 0, 1), 26, 1) == 0, "Silent icons moved");
        var iconRenderer = new SceneRenderer
        {
            Width = 960, Height = 540, ShowPreviewIcons = true,
            PreviewIcons = new(new NativeRect { Right = 3840, Bottom = 2160 }, new[] { new NativePoint(192, 216) })
        };
        iconRenderer.Measure(new(960, 540)); iconRenderer.Arrange(new(0, 0, 960, 540));
        var iconProject = new ProjectSettings { IconAmplitude = 26, IconSpread = 0 };
        var poses = new List<byte[]>();
        foreach (var beat in new[] { 0.0, .5 })
        {
            iconRenderer.Update(iconProject, new(beat, 1, 120, 0, 1)); iconRenderer.UpdateLayout();
            var bitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32); bitmap.Render(iconRenderer);
            var pixels = new byte[960 * 540 * 4]; bitmap.CopyPixels(pixels, 960 * 4, 0); poses.Add(pixels);
        }
        var top = (55 * 960 + 63) * 4; var bottom = (96 * 960 + 63) * 4;
        Assert(poses[0][top] == 0xB2 && poses[1][top] == 0x31 && poses[0][bottom] == 0x31 && poses[1][bottom] == 0xB2,
            "Rendered preview did not show the 4K icon's 52-screen-pixel / 13-preview-pixel travel.");
        report.AppendLine("PASS: icon scaling at 1920/2560/3840, resolution-independent spatial phase, silence and rendered 4K preview displacement.");
        var project = ProjectSettings.Demo();
        project.Multiplier = .5; project.Layers[1].Phase = 93;
        var path = Path.Combine(directory, "roundtrip.musicwave.json"); ProjectStore.Save(project, path);
        var loaded = ProjectStore.Load(path);
        Assert(loaded.Layers.Count == 4 && loaded.Layers[0].IsStatic && loaded.Layers[1].Phase == 93 && loaded.Multiplier == .5, "Project round trip failed");
        var render = new SceneRenderer { Width = 1280, Height = 720, ShowPreviewIcons = true };
        render.Measure(new(1280, 720)); render.Arrange(new(0, 0, 1280, 720));
        foreach (var (name, beat) in new[] { ("preview-rest.png", 0.0), ("preview-squash.png", .5) })
        {
            render.Update(ProjectSettings.Demo(), new(beat, 1, 120, 0, 1)); render.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(render);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
        }
        // Exercise transparent image compositing through the same renderer used by the desktop.
        var tiny = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
        tiny.WritePixels(new Int32Rect(0, 0, 2, 2), new byte[] { 0,0,255,255, 0,0,0,0, 0,255,0,255, 0,0,0,0 }, 8, 0);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(tiny));
        var imagePath = Path.Combine(directory, "transparent-test.png"); using (var stream = File.Create(imagePath)) png.Save(stream);
        loaded.Layers.Add(new() { Path = imagePath }); render.Validate(loaded);
        report.AppendLine("PASS: silence gating, static invariance, squash/bounce transforms, project round trip, transparent PNG decoding, rendered preview frames.");
    }
    static void DesktopSmoke(App app, string output, StringBuilder report)
    {
        var controller = new DesktopIcons(); WallpaperWindow? wallpaper = null;
        IconBackup? before = null;
        try
        {
            Assert(!File.Exists(DesktopIcons.RecoveryPath), "Pending icon recovery exists; launch the app to recover first.");
            before = DesktopIcons.Inspect();
            controller.Start();
            controller.Tick(new(.5, 1, 120, 0, 1), 24, 0);
            var during = DesktopIcons.Inspect();
            var prior = before.Icons.ToDictionary(i => i.Identity);
            var moved = during.Icons.Count(i => prior.TryGetValue(i.Identity, out var p) && i.Y != p.Y);
            Assert(before.Icons.Count == 0 || moved > 0, "No desktop icons actually moved");
            report.AppendLine($"Actual Shell movement: {moved}/{before.Icons.Count} icons");
            var project = ProjectSettings.Demo();
            var beforePixels = ProbeDesktopPixels();
            // Exercise premultiplied alpha and layer opacity on the GPU, then
            // compare the visible desktop against the WPF reference renderer.
            var transparent = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgra32, null);
            var rgba = new byte[32 * 32 * 4];
            for (var y = 0; y < 32; y++) for (var x = 0; x < 16; x++)
            {
                var index = (y * 32 + x) * 4; rgba[index + 1] = 80; rgba[index + 2] = 240; rgba[index + 3] = (byte)(y < 16 ? 255 : 128);
            }
            transparent.WritePixels(new Int32Rect(0, 0, 32, 32), rgba, 32 * 4, 0);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(transparent));
            var transparentPath = Path.Combine(Path.GetDirectoryName(output)!, "gpu-transparency.png");
            using (var file = File.Create(transparentPath)) encoder.Save(file);
            project.Layers.Add(new() { Name = "GPU alpha probe", Path = transparentPath, IsStatic = true, Width = 15, Height = 20, X = 22, Y = 15, Opacity = .65 });
            wallpaper = new(Native.Monitors()[0]); wallpaper.Attach(project);
            wallpaper.VerifyLayerOrder();
            report.AppendLine($"Wallpaper: {wallpaper.HostDescription}; verified icon > animated wallpaper > system background order.");
            report.AppendLine(Native.DescribeDesktop());
            var capturedWindow = wallpaper;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            var start = DateTime.UtcNow;
            var captured = false;
            Dictionary<(int X, int Y), uint>? restingPixels = null;
            bool motionCaptured = false;
            timer.Tick += (_, _) =>
            {
                var elapsed = (DateTime.UtcNow - start).TotalSeconds;
                try
                {
                    if (elapsed < 3)
                    {
                        // Hold two poses long enough for DWM to present them.
                        // Icons stay fixed so pixel changes prove WALLPAPER motion.
                        var frame = new MotionFrame(elapsed < 1.5 ? 0 : .5, 1, 120, 0, 1);
                        capturedWindow.Update(project, frame);
                        if (!captured && elapsed > .7)
                        {
                            var afterPixels = ProbeDesktopPixels();
                            var comparable = beforePixels.Keys.Intersect(afterPixels.Keys).ToArray();
                            var changed = comparable.Count(p => ColorDistance(beforePixels[p], afterPixels[p]) > 35);
                            report.AppendLine($"Onscreen desktop pixels: {changed}/{comparable.Length} unobscured samples changed after applying wallpaper.");
                            Assert(comparable.Length >= 16, "桌面被其他窗口遮挡，无法验证屏幕输出。请先露出部分桌面后重试。");
                            Assert(changed > comparable.Length * .15, "窗口层级正确，但桌面实际像素没有改变：壁纸仍未显示。");
                            // Pixel checks below verify presentation.
                            restingPixels = afterPixels;
                            VerifyScenePixels(project, frame, capturedWindow.Monitor, afterPixels, report);
                            captured = true;
                        }
                        if (!motionCaptured && elapsed > 2.2 && restingPixels is not null)
                        {
                            var peakPixels = ProbeDesktopPixels();
                            var comparable = restingPixels.Keys.Intersect(peakPixels.Keys).ToArray();
                            var changed = comparable.Count(p => ColorDistance(restingPixels[p], peakPixels[p]) > 20);
                            Assert(changed > 0, "GPU 提交成功，但屏幕上的壁纸没有产生动画。");
                            report.AppendLine($"PASS: onscreen wallpaper motion: {changed}/{comparable.Length} samples changed while icons stayed fixed.");
                            VerifyScenePixels(project, frame, capturedWindow.Monitor, peakPixels, report);
                            motionCaptured = true;
                            var uploads = capturedWindow.UploadCount;
                            project.Layers[1].Opacity = .6; project.Layers[1].X += 3;
                            project.Layers.Move(2, 3);
                            capturedWindow.Update(project, frame);
                            Assert(capturedWindow.UploadCount == uploads, "Position, opacity or reorder re-uploaded unchanged pixels.");
                            project.Layers[1].Width += 2;
                            capturedWindow.Update(project, frame);
                            Assert(capturedWindow.UploadCount == uploads + 1, "Resizing did not invalidate exactly one layer.");
                            project.Layers[^1].Visible = false; capturedWindow.Update(project, frame);
                            project.Layers[^1].Visible = true; capturedWindow.Update(project, frame);
                            project.Layers.RemoveAt(project.Layers.Count - 1); capturedWindow.Update(project, frame);
                            report.AppendLine("PASS: PNG alpha/opacity, position, resize, reorder, visibility and removal.");
                        }
                        return;
                    }
                    timer.Stop(); capturedWindow.Close(); controller.Stop();
                    Assert(motionCaptured, "Wallpaper motion was not verified.");
                    var after = DesktopIcons.Inspect();
                    var restored = after.Icons.All(i => !prior.TryGetValue(i.Identity, out var p) || (p.X == i.X && p.Y == i.Y));
                    Assert(restored, "Icon coordinates did not restore exactly");
                    Assert((after.Flags & 5) == (before.Flags & 5), "Auto arrange / snap flags did not restore");
                    Assert(!File.Exists(DesktopIcons.RecoveryPath), "Recovery snapshot was not cleared");
                    report.AppendLine("PASS: icon coordinates and folder flags restored exactly; wallpaper closed.");
                    File.WriteAllText(output, report.ToString()); app.Shutdown(0);
                }
                catch (Exception ex)
                {
                    timer.Stop(); capturedWindow.Close(); try { controller.Stop(); } catch (Exception restore) { report.AppendLine(restore.ToString()); }
                    report.AppendLine("FAIL: " + ex); File.WriteAllText(output, report.ToString()); app.Shutdown(1);
                }
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            wallpaper?.Close(); try { controller.Stop(); } catch (Exception restore) { report.AppendLine(restore.ToString()); }
            report.AppendLine("FAIL: " + ex); File.WriteAllText(output, report.ToString()); app.Shutdown(1);
        }
    }
    static void VerifyScenePixels(ProjectSettings project, MotionFrame frame, DesktopMonitor monitor, Dictionary<(int X, int Y), uint> actual, StringBuilder report)
    {
        var width = monitor.Bounds.Width; var height = monitor.Bounds.Height;
        var renderer = new SceneRenderer { Width = width, Height = height };
        renderer.Measure(new(width, height)); renderer.Arrange(new(0, 0, width, height));
        renderer.Update(project, frame); renderer.UpdateLayout();
        var expected = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); expected.Render(renderer);
        var pixel = new byte[4]; int matching = 0, count = 0;
        foreach (var ((x, y), color) in actual)
        {
            var px = x - monitor.Bounds.Left; var py = y - monitor.Bounds.Top;
            if (px < 0 || py < 0 || px >= width || py >= height) continue;
            expected.CopyPixels(new Int32Rect(px, py, 1, 1), pixel, 4, 0);
            var reference = (uint)(pixel[2] | pixel[1] << 8 | pixel[0] << 16);
            if (ColorDistance(color, reference) < 35) matching++;
            count++;
        }
        report.AppendLine($"GPU onscreen vs WPF reference at beat {frame.Beats:F1}: {matching}/{count} samples match.");
        Assert(count >= 16 && matching >= count * .9, "GPU 桌面画面与预览渲染不一致。");
    }
    // Sample only unobscured desktop pixels from the ACTUAL composited screen.
    // PrintWindow(Progman) cannot capture layered children and gives false negatives.
    // No content from unrelated application windows is captured or saved.
    static Dictionary<(int X, int Y), uint> ProbeDesktopPixels()
    {
        var desktop = Native.FindWindow("Progman", null);
        var result = new Dictionary<(int, int), uint>();
        var dc = GetDC(0);
        try
        {
            foreach (var monitor in Native.Monitors())
                for (var y = 0; y < 20; y++)
                    for (var x = 0; x < 32; x++)
                    {
                        var px = monitor.Bounds.Left + (int)((x + .5) / 32 * monitor.Bounds.Width);
                        var py = monitor.Bounds.Top + (int)((y + .5) / 20 * monitor.Bounds.Height);
                        var window = WindowFromPoint(new(px, py));
                        var root = GetAncestor(window, 2);
                        if (root != desktop && Native.FindWindowEx(root, 0, "SHELLDLL_DefView", null) == 0) continue;
                        var color = GetPixel(dc, px, py);
                        if (color != 0xFFFFFFFF) result[(px, py)] = color;
                    }
        }
        finally { ReleaseDC(0, dc); }
        return result;
    }
    static int ColorDistance(uint a, uint b) => Math.Abs((int)(a & 255) - (int)(b & 255)) + Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255)) + Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255));
    [DllImport("user32.dll")] static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll")] static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("gdi32.dll")] static extern uint GetPixel(nint dc, int x, int y);
    static void AudioSmoke(App app, string output, StringBuilder report)
    {
        var capture = new AudioCapture();
        try { capture.Start(""); report.AppendLine("WASAPI loopback started on the default render device."); }
        catch { capture.Dispose(); throw; }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try { var state = capture.Snapshot(); Assert(state.Error is null, state.Error ?? "Capture failed"); report.AppendLine(JsonSerializer.Serialize(state)); capture.Dispose(); report.AppendLine("PASS: loopback start, snapshot, stop/dispose."); File.WriteAllText(output, report.ToString()); app.Shutdown(0); }
            catch (Exception ex) { capture.Dispose(); report.AppendLine("FAIL: " + ex); File.WriteAllText(output, report.ToString()); app.Shutdown(1); }
        };
        timer.Start();
    }
}
