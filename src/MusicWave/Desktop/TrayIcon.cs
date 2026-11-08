using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MusicWave.Desktop;

public sealed class TrayIcon : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll")] static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    readonly HwndSource source;
    readonly Action show;
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    NotifyIconData data;
    public TrayIcon(nint hwnd, Action show)
    {
        this.show = show;
        source = HwndSource.FromHwnd(hwnd); source.AddHook(Hook);
        data = new() { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = hwnd, Id = 1, Flags = 1 | 2 | 4, Callback = 0x8001, Icon = LoadIcon(0, 32512), Tip = "MusicWave · 双击打开控制台", Info = "", Title = "" };
        Shell_NotifyIcon(0, ref data);
    }
    nint Hook(nint hwnd, int msg, nint wp, nint lp, ref bool handled)
    {
        if ((uint)msg == taskbarCreated) Shell_NotifyIcon(0, ref data);
        if (msg == 0x8001 && (lp.ToInt32() == 0x203 || lp.ToInt32() == 0x205)) { show(); handled = true; }
        return 0;
    }
    public void Dispose() { Shell_NotifyIcon(2, ref data); source.RemoveHook(Hook); }
}
