using System.ComponentModel;
using System.Runtime.InteropServices;
using MusicWave.Rendering;

namespace MusicWave.Desktop;

// A reparented WPF HWND can have correct styles/Z-order but fail to present on
// 24H2. Bind GPU composition to an independent native HWND;
// WPF never owns or rewrites this host's layered styles or its Explorer parent.
public sealed class WallpaperWindow : IDisposable
{
    const string ClassName = "MusicWave.Wallpaper.Native.v2";
    static readonly WindowProcedure procedure = WindowProc;
    static ushort registered;
    CompositionScene? scene;
    WallpaperHost? host;
    nint hwnd;
    DateTime nextLayerCheck;
    public DesktopMonitor Monitor { get; }
    public nint Handle => hwnd;
    internal long CommitCount => scene?.CommitCount ?? 0;
    internal int UploadCount => scene?.UploadCount ?? 0;
    public string HostDescription => host?.RaisedDesktop == true ? "Windows 11 GPU 图层合成" : "WorkerW GPU 图层合成";
    public WallpaperWindow(DesktopMonitor monitor) => Monitor = monitor;
    public void InvalidateImages() => scene?.InvalidateImages();

    public void Attach(ProjectSettings settings)
    {

        host = Native.FindWallpaperHost();
        if (registered == 0)
        {
            var windowClass = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Instance = GetModuleHandle(null), Procedure = Marshal.GetFunctionPointerForDelegate(procedure), Name = ClassName };
            registered = RegisterClassEx(ref windowClass);
            if (registered == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法注册原生壁纸窗口");
        }
        var origin = new NativePoint(Monitor.Bounds.Left, Monitor.Bounds.Top);
        Native.MapWindowPoints(0, host.Parent, ref origin, 1);
        hwnd = CreateWindowEx(0x80000 | 0x08000000 | 0x80 | 0x20, ClassName, "MusicWave Wallpaper", 0x40000000 | 0x04000000,
            origin.X, origin.Y, Monitor.Bounds.Width, Monitor.Bounds.Height, host.Parent, 0, GetModuleHandle(null), 0);
        if (hwnd == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建原生壁纸窗口");
        if (!Native.SetLayeredWindowAttributes(hwnd, 0, 255, 2)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法启用壁纸分层合成");

        scene = new(hwnd, Monitor.Bounds.Width, Monitor.Bounds.Height);
        if (!Native.SetWindowPos(hwnd, host.RaisedDesktop ? host.IconView : 0, origin.X, origin.Y, Monitor.Bounds.Width, Monitor.Bounds.Height, 0x10 | 0x20 | 0x40))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法定位原生壁纸窗口");
        Update(settings, new(0, 0, settings.Bpm, 0, 0));
        VerifyLayerOrder();
    }
    public void Update(ProjectSettings settings, MotionFrame frame)
    {
        if (host is null || !Native.IsWindow(host.Parent) || !Native.IsWindow(hwnd)) throw new InvalidOperationException("Windows 桌面已重新启动，请重新应用效果。");
        if (DateTime.UtcNow >= nextLayerCheck) { EnsureLayerOrder(); nextLayerCheck = DateTime.UtcNow.AddSeconds(1); }
        scene!.Update(settings, frame);
    }
    public void VerifyLayerOrder()
    {
        if (host is null || Native.GetParent(hwnd) != host.Parent || !Native.IsWindowVisible(hwnd)) throw new InvalidOperationException("壁纸窗口未正确显示。");
        if ((Native.GetWindowLongPtr(hwnd, -20).ToInt64() & 0x80000) == 0) throw new InvalidOperationException("壁纸缺少分层窗口样式。");
        if (!host.RaisedDesktop) return;
        var wallpaperSeen = false;
        for (var sibling = Native.GetWindow(host.IconView, 2); sibling != 0; sibling = Native.GetWindow(sibling, 2))
        {
            if (sibling == hwnd) wallpaperSeen = true;
            if (sibling == host.Backdrop)
            {
                if (!wallpaperSeen) throw new InvalidOperationException("壁纸被系统背景遮挡。");
                return;
            }
        }
        throw new InvalidOperationException("壁纸与图标的合成顺序无效。");
    }
    void EnsureLayerOrder()
    {
        if (host?.RaisedDesktop != true) return;
        if (!Native.IsWindow(host.IconView) || !Native.IsWindow(host.Backdrop)) throw new InvalidOperationException("Windows 桌面图层已重建，请重新应用效果。");
        // Only reposition our own HWND, never Explorer's windows.
        if (Native.GetWindow(host.IconView, 2) != hwnd && !Native.SetWindowPos(hwnd, host.IconView, 0, 0, 0, 0, 0x1 | 0x2 | 0x10)) throw new InvalidOperationException("无法调整壁纸的合成顺序。");
        VerifyLayerOrder();
    }
    public void Close() => Dispose();
    public void Dispose()
    {
        scene?.Dispose(); scene = null;
        if (hwnd != 0) { DestroyWindow(hwnd); hwnd = 0; }
        GC.SuppressFinalize(this);
    }
    static nint WindowProc(nint hwnd, uint message, nint wp, nint lp)
    {
        if (message == 0x84) return -1;
        if (message == 0x14) return 1;
        return DefWindowProc(hwnd, message, wp, lp);
    }
    delegate nint WindowProcedure(nint hwnd, uint message, nint wp, nint lp);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WindowClass
    {
        public uint Size, Style; public nint Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background;
        public string? Menu; public string Name; public nint SmallIcon;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassEx(ref WindowClass data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern nint CreateWindowEx(uint exStyle, string name, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint DefWindowProc(nint hwnd, uint message, nint wp, nint lp);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint hwnd);
}

