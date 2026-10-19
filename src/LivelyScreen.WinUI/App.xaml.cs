using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

using LivelyScreen.Native;
using System.Linq;
using System.Threading;

namespace LivelyScreen_WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private static Mutex? _singleInstanceMutex;
    private static readonly string LogFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LivelyScreen",
        "winui_debug.log"
    );

    public static void Log(string msg)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            System.IO.File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n");
        }
        catch { }
    }

    public App()
    {
        Log("WinUI App Constructor");

        bool isNewInstance = false;
        try
        {
            _singleInstanceMutex = new Mutex(true, @"Global\LivelyScreen_SingleInstance_Mutex", out isNewInstance);
        }
        catch (Exception ex)
        {
            Log($"Single instance mutex check error: {ex.Message}");
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            Log("Another LivelyScreen instance is already running. Signaling existing instance and exiting.");
            NativeMethods.PostMessage((IntPtr)NativeMethods.HWND_BROADCAST, NativeMethods.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
            Environment.Exit(0);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log($"UnhandledException: {e.ExceptionObject}");
        };
        UnhandledException += (s, e) =>
        {
            Log($"WinUI UnhandledException: {e.Message}\r\n{e.Exception}");
        };
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Log("OnLaunched enter");
        try
        {
            var cmdArgs = Environment.GetCommandLineArgs();
            bool isBackgroundLaunch = cmdArgs.Any(a =>
                a.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/autostart", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-background", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-tray", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-silent", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

            _window = new MainWindow(isBackgroundLaunch);
            Log($"MainWindow created (isBackgroundLaunch={isBackgroundLaunch})");
            _window.Closed += (s, e) =>
            {
                Log("MainWindow CLOSED event fired!");
                try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
                try { _singleInstanceMutex?.Dispose(); } catch { }
            };

            _window.Activate();
            if (isBackgroundLaunch)
            {
                Log("MainWindow launched in background (system tray only, window offscreen)");
            }
            else
            {
                Log("MainWindow activated");
            }
        }
        catch (Exception ex)
        {
            Log($"OnLaunched EXCEPTION: {ex}");
        }
    }
}
