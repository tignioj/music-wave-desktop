using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MusicWave.Desktop;

public sealed record IconPosition(string Identity, int X, int Y);
public sealed record IconBackup(uint Flags, List<IconPosition> Icons);
public sealed record IconPreviewLayout(NativeRect Bounds, IReadOnlyList<NativePoint> Positions);

public sealed class DesktopIcons : IDisposable
{
    const uint ArrangeMask = 0x1 | 0x4;
    readonly string backupPath;
    IFolderView2? view;
    nint[] pidls = [];
    NativePoint[] originals = [], animated = [];
    NativePoint screenOrigin;
    nint iconWindow;
    NativeRect[] iconMonitors = [];
    uint originalFlags;
    bool altered;
    bool hasPosition;
    internal long PositionWrites { get; private set; }
    public int Count => pidls.Length;
    public bool Active => altered;
    public static string RecoveryPath => Path.Combine(ProjectStore.DataDirectory, "desktop-recovery.json");
    public DesktopIcons(string? backupPath = null) => this.backupPath = backupPath ?? RecoveryPath;

    public IconPreviewLayout PreviewLayout(DesktopMonitor monitor)
    {
        if (Active) return BuildPreview(originals, screenOrigin, monitor);
        var v = ShellConnection.Open();
        try
        {
            var records = Read(v);
            try { return BuildPreview(records.Select(r => r.Point), ShellConnection.ScreenOrigin(), monitor); }
            finally { foreach (var r in records) Marshal.FreeCoTaskMem(r.Id); }
        }
        finally { Marshal.ReleaseComObject(v); }
    }
    static IconPreviewLayout BuildPreview(IEnumerable<NativePoint> points, NativePoint origin, DesktopMonitor monitor)
    {
        var bounds = monitor.Bounds;
        return new(bounds, points.Select(p => new NativePoint(p.X + origin.X - bounds.Left, p.Y + origin.Y - bounds.Top))
            .Where(p => p.X >= 0 && p.X < bounds.Width && p.Y >= 0 && p.Y < bounds.Height).ToArray());
    }

    public static IconBackup Inspect()
    {
        var v = ShellConnection.Open();
        try
        {
            Marshal.ThrowExceptionForHR(v.GetCurrentFolderFlags(out var flags));
            var records = Read(v);
            try { return new(flags, records.Select(r => new IconPosition(Identity(r.Id), r.Point.X, r.Point.Y)).ToList()); }
            finally { foreach (var r in records) Marshal.FreeCoTaskMem(r.Id); }
        }
        finally { Marshal.ReleaseComObject(v); }
    }
    static List<(nint Id, NativePoint Point)> Read(IFolderView2 v)
    {
        Marshal.ThrowExceptionForHR(v.ItemCount(2, out var count)); // SVGIO_ALLVIEW
        var list = new List<(nint, NativePoint)>();
        try
        {
            for (var i = 0; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(v.Item(i, out var id));
                if (v.GetItemPosition(id, out var point) < 0) { Marshal.FreeCoTaskMem(id); continue; }
                list.Add((id, point));
            }
            return list;
        }
        catch { foreach (var r in list) Marshal.FreeCoTaskMem(r.Item1); throw; }
    }
    static string Identity(nint pidl)
    {
        int length = 0, size;
        while ((size = (ushort)Marshal.ReadInt16(pidl, length)) != 0)
        { length += size; if (length > 1_048_576) throw new InvalidDataException("无效的桌面图标标识"); }
        var bytes = new byte[length + 2]; Marshal.Copy(pidl, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }
    public void Start()
    {
        if (Active) return;
        if (File.Exists(backupPath)) RestorePending(backupPath);
        view = ShellConnection.Open();
        try
        {
            Marshal.ThrowExceptionForHR(view.GetCurrentFolderFlags(out originalFlags));
            var records = Read(view);
            pidls = records.Select(r => r.Id).ToArray();
            originals = records.Select(r => r.Point).ToArray(); animated = new NativePoint[originals.Length];
            screenOrigin = ShellConnection.ScreenOrigin();
            iconWindow = ShellConnection.IconWindow();
            var monitors = Native.Monitors();
            if (monitors.Count == 0) throw new InvalidOperationException("没有可用显示器");
            iconMonitors = originals.Select(p => monitors.FirstOrDefault(m =>
                p.X + screenOrigin.X >= m.Bounds.Left && p.X + screenOrigin.X < m.Bounds.Right &&
                p.Y + screenOrigin.Y >= m.Bounds.Top && p.Y + screenOrigin.Y < m.Bounds.Bottom)?.Bounds ?? monitors[0].Bounds).ToArray();
            var backup = new IconBackup(originalFlags, records.Select(r => new IconPosition(Identity(r.Id), r.Point.X, r.Point.Y)).ToList());
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.WriteAllText(backupPath + ".tmp", JsonSerializer.Serialize(backup, ProjectStore.Json));
            File.Move(backupPath + ".tmp", backupPath, true); // Durable snapshot precedes changing Explorer.
            altered = true;
            Marshal.ThrowExceptionForHR(view.SetCurrentFolderFlags(ArrangeMask, 0));
        }
        catch { try { Stop(); } finally { Release(); } throw; }
    }
    public void Tick(MotionFrame frame, double amplitude, double spread)
    {
        if (!Active || view is null || pidls.Length == 0) return;
        if (Native.IsLeftMouseDown()) return; // Let users click/drag without fighting the animation.
        var changed = !hasPosition;
        for (var i = 0; i < originals.Length; i++)
        {
            var p = originals[i];
            var monitor = iconMonitors[i];
            // Bounce downward, so icons at the screen's top edge remain visible.
            var y = p.Y + MotionMath.IconOffset(p.X + screenOrigin.X - monitor.Left, p.Y + screenOrigin.Y - monitor.Top,
                monitor.Width, frame, amplitude, spread);
            changed |= animated[i].X != p.X || animated[i].Y != y;
            animated[i] = new(p.X, y);
        }
        if (!changed) return;
        Marshal.ThrowExceptionForHR(view.SelectAndPositionItems((uint)pidls.Length, pidls, animated, 0x80));
        PresentPositions(iconWindow);
        hasPosition = true; PositionWrites++;
    }
    static void PresentPositions(nint window)
    {
        if (!Native.IsWindow(window)) throw new InvalidOperationException("桌面图标窗口已重建，请重新应用效果。");
        if (!Native.IsWindowVisible(window)) return;
        // Explorer can update IFolderView's coordinates while retaining the old
        // list-view image (reproduced on Windows 11 24H2). WM_SETREDRAW(TRUE)
        // flushes the control's deferred layout after the batch. Invalidating or
        // RedrawWindow alone does not present those pending icon positions.
        // Never enable redraw on a hidden view: that would show hidden icons.
        if (Native.SendMessageTimeout(window, 0xB, 1, 0, 0x3, 200, out _) == 0)
            throw new InvalidOperationException("Explorer 图标画面更新超时，请重新应用效果。");
    }
    public void Stop()
    {
        if (!altered) { Release(); return; }
        try
        {
            // Re-enumerating by PIDL prevents deleted/new items from receiving another icon's coordinates.
            RestorePending(backupPath);
            altered = false;
        }
        finally { Release(); }
    }
    public static int RestorePending(string? file = null)
    {
        file ??= RecoveryPath;
        if (!File.Exists(file)) return 0;
        var backup = JsonSerializer.Deserialize<IconBackup>(File.ReadAllText(file), ProjectStore.Json) ?? throw new InvalidDataException("图标恢复文件无效");
        var v = ShellConnection.Open();
        try
        {
            var records = Read(v);
            try
            {
                var saved = backup.Icons.ToDictionary(i => i.Identity);
                var matching = records.Where(r => saved.ContainsKey(Identity(r.Id))).ToArray();
                Marshal.ThrowExceptionForHR(v.SetCurrentFolderFlags(ArrangeMask, 0));
                try
                {
                    if (matching.Length > 0)
                    {
                        Marshal.ThrowExceptionForHR(v.SelectAndPositionItems((uint)matching.Length,
                            matching.Select(r => r.Id).ToArray(), matching.Select(r => { var p = saved[Identity(r.Id)]; return new NativePoint(p.X, p.Y); }).ToArray(), 0x80));
                        PresentPositions(ShellConnection.IconWindow());
                    }
                }
                finally { Marshal.ThrowExceptionForHR(v.SetCurrentFolderFlags(ArrangeMask, backup.Flags & ArrangeMask)); }
                File.Delete(file);
                return matching.Length;
            }
            finally { foreach (var r in records) Marshal.FreeCoTaskMem(r.Id); }
        }
        finally { Marshal.ReleaseComObject(v); }
    }
    void Release()
    {
        foreach (var id in pidls) Marshal.FreeCoTaskMem(id);
        pidls = []; originals = []; animated = []; iconMonitors = [];
        iconWindow = 0;
        hasPosition = false;
        if (view is not null) { Marshal.ReleaseComObject(view); view = null; }
    }
    public void Dispose() { Stop(); GC.SuppressFinalize(this); }
}
