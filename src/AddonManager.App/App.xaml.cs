using System.Windows;
using AddonManager.App.Session;
using AddonManager.App.Startup;
using AddonManager.App.Thumbnails;
using AddonManager.App.ViewModels;
using AddonManager.Infrastructure;
using AddonManager.Sim2020;
using Microsoft.Win32;

namespace AddonManager.App;

/// <summary>The WPF application: single instance, then the session, the view models and the window.</summary>
public partial class App : Application
{
    private SingleInstance? guard;
    private AppSession? session;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // An exception on the UI thread would otherwise end the process without a word. Writes no file.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            MessageBox.Show($"Something went wrong: {args.Exception.Message}", "Modern MSFS Addon Manager");
            Shutdown();
        };

        var second = AppStartup.AcquireOrSignal(SingleInstance.AppName, BringToFront, out guard);
        if (second == SecondStart.OtherSession)
            MessageBox.Show("Modern MSFS Addon Manager is already running in another Windows session.", "Modern MSFS Addon Manager");
        if (second == SecondStart.Elevated)
            MessageBox.Show("Modern MSFS Addon Manager is already running as administrator. Close it there first, or start this one as administrator too.", "Modern MSFS Addon Manager");
        if (second is not null)
        {
            Shutdown();
            return;
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        var databaseFile = AppStartup.DatabaseFile(e.Args);
        session = new AppSession(() => AppServices.Open(databaseFile), () => InstallLocator.Locate(roaming, local), AppPaths.GsxProfileFolder);

        Action<Action> post = action => Dispatcher.InvokeAsync(action);
        var thumbnails = new ThumbnailCache();
        var main = new MainViewModel(session, post, AppPaths.GsxProfileFolder, thumbnails.LoadAsync);
        var firstRun = new FirstRunViewModel(session, post, PickFolder);

        MainWindow = new MainWindow(main, firstRun);
        MainWindow.Show();
        _ = session.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        session?.Dispose();
        guard?.Dispose();
        base.OnExit(e);
    }

    private static string? PickFolder()
    {
        var dialog = new OpenFolderDialog();
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>Runs on a thread-pool thread when a second start signals; does nothing before the window exists.</summary>
    private void BringToFront() => Dispatcher.InvokeAsync(() =>
    {
        if (MainWindow is not { } window)
            return;

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        // Activate alone only flashes the taskbar button when another app is in front; toggling Topmost brings the window up.
        window.Topmost = true;
        window.Topmost = false;
    });
}
