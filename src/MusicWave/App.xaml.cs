using System.IO;
using System.Windows;

namespace MusicWave;

public partial class App : Application
{
    Mutex? singleton;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Any(a => a.StartsWith("--")))
        {
            Diagnostics.Run(this, e.Args); return;
        }
        singleton = new(true, "Local\\MusicWave.Desktop.v1", out var created);
        if (!created) { MessageBox.Show("MusicWave 已在运行，请双击系统托盘图标打开控制台。", "MusicWave"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            Log("界面异常", args.Exception);
            try { (MainWindow as MainWindow)?.StopDesktop(); } catch { /* Recovery snapshot remains on disk. */ }
            args.Handled = true;
            MessageBox.Show("程序遇到错误，已停止桌面动画。详细信息：" + args.Exception.Message, "MusicWave");
            Shutdown(1);
        };
        try { MainWindow = new MainWindow(); MainWindow.Show(); }
        catch (Exception ex) { Log("启动失败", ex); MessageBox.Show(ex.Message, "MusicWave 启动失败"); Shutdown(1); }
    }
    public static void Log(string context, Exception ex)
    {
        try { Directory.CreateDirectory(ProjectStore.DataDirectory); File.AppendAllText(Path.Combine(ProjectStore.DataDirectory, "error.log"), $"[{DateTimeOffset.Now:O}] {context}\n{ex}\n"); } catch { }
    }
    protected override void OnExit(ExitEventArgs e) { singleton?.Dispose(); base.OnExit(e); }
}
