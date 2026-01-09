using System.Threading;
using System.Windows;

namespace MediaRPC;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;
    private const string MutexName = "MediaRPC_SingleInstance_Mutex";

    public static bool IsStartupMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Check for single instance
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        
        if (!createdNew)
        {
            // Another instance is already running
            MessageBox.Show("MediaRPC is already running.", "MediaRPC", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Check for startup mode (launched from Windows startup)
        IsStartupMode = e.Args.Contains("--startup");

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
