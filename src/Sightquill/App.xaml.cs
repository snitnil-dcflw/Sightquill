using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sightquill;
public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? activationWait;
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    protected override void OnStartup(StartupEventArgs e)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        base.OnStartup(e);
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Sightquill.Activate");
        instance = new Mutex(true, "Local\\Sightquill.Desktop", out var first);
        if (!first)
        {
            using var current = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(current.ProcessName))
                using (process)
                {
                    try { if (process.Id != current.Id && process.SessionId == current.SessionId) AllowSetForegroundWindow(process.Id); }
                    catch (InvalidOperationException) { }
                }
            activation.Set(); Shutdown(); return;
        }
        DispatcherUnhandledException += (_, args) => {
            try {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sightquill");
                Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, "error.log"), DateTime.Now + "\n" + args.Exception + "\n");
            } catch { }
            MessageBox.Show("Sightquill needs to close. A log was saved in %LOCALAPPDATA%\\Sightquill\\error.log.", "Sightquill");
            args.Handled = true; Shutdown(1);
        };
        MainWindow = new MainWindow(); MainWindow.Show();
        activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() => { if (MainWindow is MainWindow window && !Dispatcher.HasShutdownStarted) window.RestoreWindow(); });
        }, null, Timeout.Infinite, false);
    }
    protected override void OnExit(ExitEventArgs e) { activationWait?.Unregister(null); activation?.Dispose(); instance?.Dispose(); base.OnExit(e); }
}
