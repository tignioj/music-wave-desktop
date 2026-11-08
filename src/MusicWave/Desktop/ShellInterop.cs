using System.Runtime.InteropServices;

namespace MusicWave.Desktop;

[StructLayout(LayoutKind.Sequential)]
public struct NativePoint { public int X; public int Y; public NativePoint(int x, int y) { X = x; Y = y; } }

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellServiceProvider
{
    [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
}

[ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellBrowser
{
    [PreserveSig] int GetWindow(out nint hwnd);
    [PreserveSig] int ContextSensitiveHelp(bool enter);
    [PreserveSig] int InsertMenusSB(nint menu, nint widths);
    [PreserveSig] int SetMenuSB(nint menu, nint oleMenu, nint hwnd);
    [PreserveSig] int RemoveMenusSB(nint menu);
    [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
    [PreserveSig] int EnableModelessSB(bool enable);
    [PreserveSig] int TranslateAcceleratorSB(nint msg, ushort id);
    [PreserveSig] int BrowseObject(nint pidl, uint flags);
    [PreserveSig] int GetViewStateStream(uint mode, out nint stream);
    [PreserveSig] int GetControlWindow(uint id, out nint hwnd);
    [PreserveSig] int SendControlMsg(uint id, uint msg, nint wParam, nint lParam, out nint result);
    [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object view);
}

// Flattened inherited IFolderView vtable, followed by IFolderView2 methods in Windows SDK order.
[ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IFolderView2
{
    [PreserveSig] int GetCurrentViewMode(out uint mode);
    [PreserveSig] int SetCurrentViewMode(uint mode);
    [PreserveSig] int GetFolder(ref Guid iid, out nint folder);
    [PreserveSig] int Item(int index, out nint pidl);
    [PreserveSig] int ItemCount(uint flags, out int count);
    [PreserveSig] int Items(uint flags, ref Guid iid, out nint items);
    [PreserveSig] int GetSelectionMarkedItem(out int index);
    [PreserveSig] int GetFocusedItem(out int index);
    [PreserveSig] int GetItemPosition(nint pidl, out NativePoint point);
    [PreserveSig] int GetSpacing(out NativePoint point);
    [PreserveSig] int GetDefaultSpacing(out NativePoint point);
    [PreserveSig] int GetAutoArrange();
    [PreserveSig] int SelectItem(int index, uint flags);
    [PreserveSig] int SelectAndPositionItems(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] pidls, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] NativePoint[] points, uint flags);
    [PreserveSig] int SetGroupBy(nint key, bool ascending);
    [PreserveSig] int GetGroupBy(nint key, nint ascending);
    [PreserveSig] int SetViewProperty(nint pidl, nint key, nint value);
    [PreserveSig] int GetViewProperty(nint pidl, nint key, nint value);
    [PreserveSig] int SetTileViewProperties(nint pidl, nint list);
    [PreserveSig] int SetExtendedTileViewProperties(nint pidl, nint list);
    [PreserveSig] int SetText(int type, nint text);
    [PreserveSig] int SetCurrentFolderFlags(uint mask, uint flags);
    [PreserveSig] int GetCurrentFolderFlags(out uint flags);
}

static class ShellConnection
{
    public static nint IconWindow()
    {
        var defView = Native.FindWindowEx(Native.FindWindow("Progman", null), 0, "SHELLDLL_DefView", null);
        if (defView == 0)
            Native.EnumWindows((window, _) =>
            {
                defView = Native.FindWindowEx(window, 0, "SHELLDLL_DefView", null);
                return defView == 0;
            }, 0);
        if (defView == 0) throw new InvalidOperationException("未找到桌面图标窗口");
        var hwnd = Native.FindWindowEx(defView, 0, "SysListView32", null);
        if (hwnd == 0) hwnd = defView;
        return hwnd;
    }
    public static NativePoint ScreenOrigin()
    {
        var origin = new NativePoint();
        Native.MapWindowPoints(IconWindow(), 0, ref origin, 1);
        return origin;
    }
    public static IFolderView2 Open()
    {
        object? windows = null, dispatch = null, browser = null;
        try
        {
            windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true)!)!;
            dynamic shellWindows = windows;
            object location = 0, empty = null!;
            int hwnd;
            dispatch = shellWindows.FindWindowSW(ref location, ref empty, 8, out hwnd, 1);
            if (dispatch is null) throw new InvalidOperationException("无法连接 Windows 桌面，请确认 explorer.exe 正在运行。");
            var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
            var iid = new Guid("000214E2-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(((IShellServiceProvider)dispatch).QueryService(ref service, ref iid, out browser));
            Marshal.ThrowExceptionForHR(((IShellBrowser)browser).QueryActiveShellView(out var view));
            try { return (IFolderView2)view; }
            catch { Marshal.ReleaseComObject(view); throw; }
        }
        finally
        {
            if (browser is not null) Marshal.ReleaseComObject(browser);
            if (dispatch is not null) Marshal.ReleaseComObject(dispatch);
            if (windows is not null) Marshal.ReleaseComObject(windows);
        }
    }
}
