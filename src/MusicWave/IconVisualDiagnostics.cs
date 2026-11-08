using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicWave.Desktop;

namespace MusicWave;

internal static class IconVisualDiagnostics
{
    public static void Check(App app, string output, StringBuilder report)
    {
        var controller = new DesktopIcons();
        WallpaperWindow? wallpaper = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        try
        {
            if (File.Exists(DesktopIcons.RecoveryPath)) throw new InvalidOperationException("Stop MusicWave before checking actual icon pixels.");
            var before = DesktopIcons.Inspect();
            var monitor = Native.Monitors()[0];
            var origin = ShellConnection.ScreenOrigin();
            var icon = before.Icons.OrderBy(i => i.Y).ThenBy(i => i.X).First(i => i.X + origin.X >= monitor.Bounds.Left && i.X + origin.X < monitor.Bounds.Right);
            var travel = (int)Math.Round(26 * monitor.Bounds.Width / 1920.0);
            var rect = new NativeRect { Left = icon.X + origin.X, Top = icon.Y + origin.Y,
                Right = icon.X + origin.X + 112, Bottom = icon.Y + origin.Y + 112 + travel };
            // A uniform static background makes image matching depend on the real
            // icon pixels, independent of wallpaper movement or texture changes.
            var fixture = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                Enumerable.Range(0, 4).SelectMany(_ => new byte[] { 49, 29, 16, 255 }).ToArray(), 8);
            var backgroundPath = Path.Combine(Path.GetDirectoryName(output)!, "icon-visual-background.png");
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(fixture));
            using (var file = File.Create(backgroundPath)) png.Save(file);
            var project = new ProjectSettings();
            project.Layers.Add(new() { Path = backgroundPath, IsStatic = true, Width = 100, Height = 100, X = 50, Y = 100 });
            wallpaper = new(monitor); wallpaper.Attach(project); controller.Start();
            controller.Tick(new(0, 1, 120, 0, 1), 26, 0);
            byte[]? resting = null;
            List<int> mask = [];
            var step = 0;
            timer.Tick += (_, _) =>
            {
                try
                {
                    var bitmap = Capture(rect);
                    var pixels = new byte[rect.Width * rect.Height * 4]; bitmap.CopyPixels(pixels, rect.Width * 4, 0);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var pose = step == 1 ? "peak" : step == 0 ? "rest" : "return";
                    using (var file = File.Create(Path.Combine(Path.GetDirectoryName(output)!, $"{Path.GetFileNameWithoutExtension(output)}-{pose}.png"))) encoder.Save(file);
                    if (step == 0)
                    {
                        resting = pixels;
                        for (var y = 8; y < 96; y++) for (var x = 8; x < 96; x++)
                        {
                            var index = (y * rect.Width + x) * 4;
                            if (Math.Abs(pixels[index] - 49) + Math.Abs(pixels[index + 1] - 29) + Math.Abs(pixels[index + 2] - 16) > 60) mask.Add(index);
                        }
                        if (mask.Count < 100) throw new InvalidOperationException("No visible icon artwork in the screen probe.");
                        controller.Tick(new(.5, 1, 120, 0, 1), 26, 0);
                    }
                    else if (step == 1)
                    {
                        var scores = Enumerable.Range(0, travel + 9).Select(offset =>
                        {
                            var shift = offset * rect.Width * 4;
                            var error = mask.Sum(index => Math.Abs(resting![index] - pixels[index + shift]) + Math.Abs(resting[index + 1] - pixels[index + shift + 1]) + Math.Abs(resting[index + 2] - pixels[index + shift + 2])) / (3.0 * mask.Count);
                            return (Offset: offset, Error: error);
                        }).OrderBy(s => s.Error).ToArray();
                        var best = scores[0];
                        report.AppendLine($"Actual screen icon image: expected travel={travel}px, measured={best.Offset}px, matched pixels={mask.Count}, mean channel error={best.Error:F3}/255.");
                        if (Math.Abs(best.Offset - travel) > 1 || best.Error > 10) throw new InvalidOperationException("Shell positions changed, but the actual screen icon image did not follow.");
                        controller.Tick(new(0, 1, 120, 0, 1), 26, 0);
                    }
                    else
                    {
                        var error = mask.Sum(index => Math.Abs(resting![index] - pixels[index]) + Math.Abs(resting[index + 1] - pixels[index + 1]) + Math.Abs(resting[index + 2] - pixels[index + 2])) / (3.0 * mask.Count);
                        if (error > 10) throw new InvalidOperationException("The displayed icon image did not return to its original position.");
                        timer.Stop(); controller.Stop(); wallpaper.Dispose();
                        var after = DesktopIcons.Inspect();
                        if (after.Flags != before.Flags || !after.Icons.SequenceEqual(before.Icons)) throw new InvalidOperationException("Desktop coordinates or flags were not restored exactly.");
                        report.AppendLine($"PASS: real screen icon moves and returns (return error={error:F3}/255); coordinates and flags restored exactly.");
                        File.WriteAllText(output, report.ToString()); app.Shutdown(0);
                    }
                    step++;
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
            wallpaper?.Dispose(); report.AppendLine("FAIL: " + ex); File.WriteAllText(output, report.ToString()); app.Shutdown(1);
        }
    }
    // Capture only an unobscured desktop area containing the icons. Unlike a
    // Shell coordinate readback, these pixels come from DWM's presented screen.
    public static void Observe(App app, string output, StringBuilder report)
    {
        var monitor = Native.Monitors()[0].Bounds;
        var origin = ShellConnection.ScreenOrigin();
        var icons = DesktopIcons.Inspect().Icons;
        if (icons.Count == 0) throw new InvalidOperationException("No icons to observe.");
        var rect = new NativeRect
        {
            Left = Math.Max(monitor.Left, icons.Min(i => i.X) + origin.X - 8),
            Top = Math.Max(monitor.Top, icons.Min(i => i.Y) + origin.Y - 160),
            Right = Math.Min(monitor.Right, icons.Max(i => i.X) + origin.X + 160),
            Bottom = Math.Min(monitor.Bottom, icons.Max(i => i.Y) + origin.Y + 280)
        };
        rect = UnobscuredBounds(rect);
        report.AppendLine($"Screen capture: {rect.Left},{rect.Top} {rect.Width}x{rect.Height}; {icons.Count} icons.");
        report.AppendLine(Native.DescribeDesktop());
        var step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        var watch = Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            try
            {
                var snapshot = DesktopIcons.Inspect();
                var bitmap = Capture(rect);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var fileName = Path.Combine(Path.GetDirectoryName(output)!, $"{Path.GetFileNameWithoutExtension(output)}-{step:D2}.png");
                using (var file = File.Create(fileName)) encoder.Save(file);
                report.AppendLine($"t={watch.Elapsed.TotalSeconds:F3}, positions={string.Join("; ", snapshot.Icons.Select(i => $"{i.X},{i.Y}"))}; pixels={Path.GetFileName(fileName)}");
                if (++step < 12) return;
                timer.Stop(); report.AppendLine("Captured actual desktop frames; coordinate movement alone is not a visual PASS.");
                File.WriteAllText(output, report.ToString()); app.Shutdown(0);
            }
            catch (Exception ex) { timer.Stop(); report.AppendLine("FAIL: " + ex); File.WriteAllText(output, report.ToString()); app.Shutdown(1); }
        };
        timer.Start();
    }
    internal static BitmapSource Capture(NativeRect rect)
    {
        // Reject capture if another application's window covers this desktop area.
        for (var y = rect.Top; y < rect.Bottom; y += 24)
            for (var x = rect.Left; x < rect.Right; x += 24)
            {
                var root = GetAncestor(WindowFromPoint(new(x, y)), 2);
                if (root != Native.FindWindow("Progman", null) && Native.FindWindowEx(root, 0, "SHELLDLL_DefView", null) == 0)
                {
                    Native.GetWindowRect(root, out var covering);
                    throw new InvalidOperationException($"桌面图标区域被其他窗口遮挡：point={x},{y}, root=0x{root:X}, rect={covering.Left},{covering.Top} {covering.Width}x{covering.Height}。");
                }
            }
        var dc = GetDC(0); var memory = CreateCompatibleDC(dc); var bitmap = CreateCompatibleBitmap(dc, rect.Width, rect.Height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, rect.Width, rect.Height, dc, rect.Left, rect.Top, 0x40CC0020)) throw new InvalidOperationException("Screen capture failed.");
            var result = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            result.Freeze(); return result;
        }
        finally { SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(0, dc); }
    }
    static NativeRect UnobscuredBounds(NativeRect rect)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var clipped = false;
            for (var y = rect.Top; y < rect.Bottom && !clipped; y += 24)
                for (var x = rect.Left; x < rect.Right && !clipped; x += 24)
                {
                    var root = GetAncestor(WindowFromPoint(new(x, y)), 2);
                    if (root == Native.FindWindow("Progman", null) || Native.FindWindowEx(root, 0, "SHELLDLL_DefView", null) != 0) continue;
                    Native.GetWindowRect(root, out var covering);
                    if (covering.Top > rect.Top + 160) rect.Bottom = Math.Min(rect.Bottom, covering.Top);
                    else if (covering.Left > rect.Left + 160) rect.Right = Math.Min(rect.Right, covering.Left);
                    else throw new InvalidOperationException("没有足够未遮挡的桌面图标区域。");
                    clipped = true;
                }
            if (!clipped) return rect;
        }
        throw new InvalidOperationException("没有足够未遮挡的桌面图标区域。");
    }
    [DllImport("user32.dll")] static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll")] static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
}
