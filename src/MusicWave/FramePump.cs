using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace MusicWave;

// A native high-resolution clock keeps running when the console is hidden.
// At most one dispatcher operation is queued; late frames are dropped rather
// than played back in a burst, and deadlines don't drift by the render cost.
internal sealed class FramePump(Dispatcher dispatcher, Action tick) : IDisposable
{
    readonly ManualResetEvent stop = new(false);
    Thread? thread;
    int pending;
    bool disposed;
    public void Start()
    {
        if (thread is not null) return;
        thread = new Thread(Run) { IsBackground = true, Name = "MusicWave frame clock" };
        thread.Start();
    }
    void Run()
    {
        using var timer = new EventWaitHandle(false, EventResetMode.AutoReset);
        var handle = CreateWaitableTimerEx(0, null, 2, 0x1F0003);
        if (handle.IsInvalid) { handle.Dispose(); handle = CreateWaitableTimerEx(0, null, 0, 0x1F0003); }
        if (handle.IsInvalid) { App.Log("创建动画时钟", new Win32Exception(Marshal.GetLastWin32Error())); handle.Dispose(); return; }
        timer.SafeWaitHandle = handle;
        WaitHandle[] waits = [stop, timer];
        var watch = Stopwatch.StartNew();
        double next = 0;
        while (!stop.WaitOne(0))
        {
            next += 1.0 / 60;
            var remaining = next - watch.Elapsed.TotalSeconds;
            if (remaining <= 0) { next = watch.Elapsed.TotalSeconds; remaining = 1.0 / 60; }
            long due = -Math.Max(1, (long)(remaining * 10_000_000));
            if (!SetWaitableTimer(handle, ref due, 0, 0, 0, false)) { App.Log("设置动画时钟", new Win32Exception(Marshal.GetLastWin32Error())); return; }
            if (WaitHandle.WaitAny(waits) == 0) return;
            if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) continue;
            dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                try { if (!disposed) tick(); }
                finally { Volatile.Write(ref pending, 0); }
            });
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; stop.Set(); thread?.Join(); stop.Dispose();
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeWaitHandle CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, nint callback, nint argument, bool resume);
}
