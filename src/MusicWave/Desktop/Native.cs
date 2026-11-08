using System.Runtime.InteropServices;
using System.Text;

namespace MusicWave.Desktop;

[StructLayout(LayoutKind.Sequential)]
public struct NativeRect { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
public sealed record DesktopMonitor(string Name, NativeRect Bounds) { public override string ToString() => Name; }
public sealed record WallpaperHost(nint Parent, nint IconView, nint Backdrop, bool RaisedDesktop);

public static class Native
{
    public delegate bool EnumWindowProc(nint hwnd, nint param);
    delegate bool MonitorProc(nint monitor, nint dc, ref NativeRect rect, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindow(string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowProc proc, nint data);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetParent(nint hwnd, nint parent);
    [DllImport("user32.dll")] public static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] public static extern int MapWindowPoints(nint from, nint to, ref NativePoint point, uint count);
    [DllImport("user32.dll")] public static extern bool GetClientRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] public static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wp, nint lp, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorProc proc, nint data);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, uint size);
    public static string DescribeDesktop()
    {
        var report = new StringBuilder();
        void Describe(nint hwnd, int depth)
        {
            var name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity);
            var title = new StringBuilder(256); GetWindowText(hwnd, title, title.Capacity);
            GetWindowRect(hwnd, out var r); GetWindowThreadProcessId(hwnd, out var pid);
            DwmGetWindowAttribute(hwnd, 14, out var cloaked, 4);
            report.AppendLine($"{new string(' ', depth * 2)}0x{hwnd:X} {name} title={title} pid={pid} visible={IsWindowVisible(hwnd)} cloaked={cloaked} rect={r.Left},{r.Top} {r.Width}x{r.Height} style=0x{GetWindowLongPtr(hwnd, -16):X} ex=0x{GetWindowLongPtr(hwnd, -20):X}");
            if (depth >= 5) return;
            for (nint child = FindWindowEx(hwnd, 0, null, null); child != 0; child = FindWindowEx(hwnd, child, null, null)) Describe(child, depth + 1);
        }
        EnumWindows((hwnd, _) =>
        {
            var name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity);
            var title = new StringBuilder(256); GetWindowText(hwnd, title, title.Capacity);
            GetWindowRect(hwnd, out var bounds);
            if (name.ToString() == "Progman" || (name.ToString() == "WorkerW" && IsWindowVisible(hwnd) && bounds.Width > 500)
                || title.ToString() is "MusicWave · 音乐律动桌面" or "MusicWave Wallpaper") Describe(hwnd, 0);
            return true;
        }, 0);
        return report.ToString();
    }
    public static bool IsLeftMouseDown() => (GetAsyncKeyState(1) & 0x8000) != 0;
    public static List<DesktopMonitor> Monitors()
    {
        var list = new List<DesktopMonitor>();
        EnumDisplayMonitors(0, 0, (nint m, nint dc, ref NativeRect r, nint data) => { list.Add(new($"显示器 {list.Count + 1} · {r.Width} × {r.Height}", r)); return true; }, 0);
        return list;
    }
    public static WallpaperHost FindWallpaperHost()
    {
        var progman = FindWindow("Progman", null);
        if (progman == 0) throw new InvalidOperationException("未找到 Windows 桌面。");
        SendMessageTimeout(progman, 0x052C, 0xD, 1, 2, 1000, out _);
        // Windows 11 24H2 has a single composited Progman hierarchy. Its child
        // WorkerW paints the ORIGINAL wallpaper: parenting inside it is invisible.
        // Supply a layered sibling between DefView and WorkerW instead.
        var iconView = FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        var backdrop = FindWindowEx(progman, 0, "WorkerW", null);
        if ((GetWindowLongPtr(progman, -20).ToInt64() & 0x200000) != 0)
        {
            if (iconView == 0 || backdrop == 0) throw new InvalidOperationException("Windows 桌面合成层未就绪，请重试。");
            return new(progman, iconView, backdrop, true);
        }
        nint host = 0;
        EnumWindows((hwnd, _) =>
        {
            if (FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null) == 0) return true;
            host = FindWindowEx(0, hwnd, "WorkerW", null);
            return host == 0;
        }, 0);
        if (host == 0 || FindWindowEx(host, 0, "SHELLDLL_DefView", null) != 0)
            throw new InvalidOperationException("系统未提供独立壁纸层。请退出其他动态壁纸软件后重试。");
        return new(host, 0, host, false);
    }
    public static bool HasFullscreenApp(NativeRect monitor)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0 || hwnd == FindWindow("Progman", null) || hwnd == FindWindow("WorkerW", null)) return false;
        return GetWindowRect(hwnd, out var r) && r.Left <= monitor.Left && r.Top <= monitor.Top && r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;
    }
}
