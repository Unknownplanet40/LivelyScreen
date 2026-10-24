using Windows.Graphics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using LivelyScreen.Models;
using LivelyScreen.Native;
using LivelyScreen.Services;
using LivelyScreen_WinUI.Services;
using LivelyScreen_WinUI.Views;

namespace LivelyScreen_WinUI;

public sealed partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly MediaLibrary _mediaLibrary;
    private readonly IdleDetector _idleDetector;
    private readonly ScreensaverManager _screensaverManager;
    private readonly UpdateService _updateService;

    private NativeMethods.NOTIFYICONDATA _nid;
    private NativeMethods.SubclassProc? _subclassProc;
    private IntPtr _hTrayIcon = IntPtr.Zero;
    private IntPtr _hBalloonIcon = IntPtr.Zero;

    private const uint CMD_TRAY_OPEN = 10001;
    private const uint CMD_TRAY_START = 10002;
    private const uint CMD_TRAY_NEXT = 10003;
    private const uint CMD_TRAY_EXIT = 10004;

    public MainWindow() : this(false)
    {
    }

    public MainWindow(bool startMinimized)
    {
        App.Log($"MainWindow ctor enter (startMinimized={startMinimized})");
        InitializeComponent();
        App.Log("MainWindow InitializeComponent done");

        _settingsService = new SettingsService();
        _mediaLibrary = new MediaLibrary(_settingsService);
        _idleDetector = new IdleDetector(_settingsService);
        _screensaverManager = new ScreensaverManager(_settingsService, _mediaLibrary, _idleDetector, DispatcherQueue);
        _updateService = new UpdateService(DispatcherQueue);
        App.Log("MainWindow services initialized");

        // Initialize Native Windows 11 AppWindow properties
        AppWindow.Title = "LivelyScreen - Video & Photo Screensaver";
        AppWindow.Resize(new SizeInt32(1000, 660));

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        App.Log($"MainWindow AppWindow ID={AppWindow.Id.Value}, Title set");

        var hWnd = WindowNative.GetWindowHandle(this);

        // Stealth tray startup: force disable DWM transitions, set 0 alpha transparency, and move offscreen
        try
        {
            if (startMinimized)
            {
                // Disable DWM window transition animations completely
                int forceDisable = 1;
                NativeMethods.DwmSetWindowAttribute(hWnd, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, ref forceDisable, sizeof(int));

                // 100% transparent layered window at DWM compositor level - 0 pixels drawn
                var exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE, (IntPtr)(exStyle.ToInt64() | NativeMethods.WS_EX_LAYERED));
                NativeMethods.SetLayeredWindowAttributes(hWnd, 0, 0, NativeMethods.LWA_ALPHA);

                AppWindow.IsShownInSwitchers = false;
                AppWindow.Move(new PointInt32(-32000, -32000));
            }
            else
            {
                AppWindow.IsShownInSwitchers = true;
                CenterWindowOnPrimary();
            }
        }
        catch { }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
        }

        if (!startMinimized)
        {
            NativeMethods.ShowWindow(hWnd, 5); // SW_SHOW
            NativeMethods.SetForegroundWindow(hWnd);
        }
        else
        {
            App.Log("MainWindow launched minimized to system tray (stealth DWM 0-alpha mode)");
        }

        AutoStartService.SyncAutoStartRegistration();

        // Minimize to tray on close
        AppWindow.Closing += (s, e) =>
        {
            App.Log("AppWindow Closing event triggered - minimizing to tray");
            e.Cancel = true;
            AppWindow.IsShownInSwitchers = false;
            NativeMethods.SetLayeredWindowAttributes(hWnd, 0, 0, NativeMethods.LWA_ALPHA);
            AppWindow.Move(new PointInt32(-32000, -32000));
        };

        Closed += (s, e) =>
        {
            CleanupTrayAndHooks();
        };

        SetupTrayIcon();
        App.Log("MainWindow SetupTrayIcon done");
        RegisterGlobalHotkey();
        App.Log("MainWindow RegisterGlobalHotkey done");

        // Initialize hosted MainPage with services and window
        MainView.InitializeServices(this, _settingsService, _mediaLibrary, _idleDetector, _screensaverManager, _updateService);
        MainView.ThemePreferenceChanged += ApplyThemePreference;
        MainView.ScreensaverRequested += () =>
        {
            NativeMethods.AllowSetForegroundWindow(Environment.ProcessId);
            NativeMethods.LockSetForegroundWindow(NativeMethods.LSFW_UNLOCK);
            _screensaverManager.StartScreensaver();
        };

        ApplyThemePreference();

        _idleDetector.Start();
        App.Log("MainWindow _idleDetector started");
        _updateService.StartBackgroundMonitoring();
        App.Log("MainWindow _updateService monitoring started");
    }

    private void SetupTrayIcon()
    {
        try
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LogoMark.png");
            if (!System.IO.File.Exists(iconPath))
            {
                iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LogoMark.ico");
            }
            if (!System.IO.File.Exists(iconPath))
            {
                iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            }

            _hTrayIcon = NativeMethods.LoadIconFromFile(iconPath, 16, 16);
            _hBalloonIcon = NativeMethods.LoadIconFromFile(iconPath, 48, 48);
            if (_hBalloonIcon == IntPtr.Zero)
            {
                _hBalloonIcon = NativeMethods.LoadIconFromFile(iconPath, 32, 32);
            }
            if (_hTrayIcon == IntPtr.Zero)
            {
                _hTrayIcon = NativeMethods.LoadIcon(IntPtr.Zero, NativeMethods.IDI_APPLICATION);
            }
            if (_hBalloonIcon == IntPtr.Zero)
            {
                _hBalloonIcon = _hTrayIcon;
            }

            // Install window subclass to intercept WM_TRAYICON and WM_HOTKEY messages
            _subclassProc = WindowSubclassHandler;
            NativeMethods.SetWindowSubclass(hWnd, _subclassProc, (UIntPtr)1001, UIntPtr.Zero);

            _nid = new NativeMethods.NOTIFYICONDATA
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONDATA)),
                hWnd = hWnd,
                uID = 1001,
                uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
                uCallbackMessage = NativeMethods.WM_TRAYICON,
                hIcon = _hTrayIcon,
                hBalloonIcon = _hBalloonIcon,
                dwInfoFlags = NativeMethods.NIIF_USER | NativeMethods.NIIF_LARGE_ICON,
                szTip = "LivelyScreen - Video & Photo Screensaver",
            };

            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _nid);
            App.Log("Tray icon registered and window subclass hooked successfully");

            // Show rich Windows 11 Toast Notification with hero banner and circular app logo
            ToastNotificationService.ShowToastWithBanner(
                "LivelyScreen",
                "Click the tray icon to open LivelyScreen or right-click for more options."
            );
        }
        catch (Exception ex)
        {
            App.Log($"Failed to setup tray icon: {ex.Message}");
        }
    }

    private IntPtr WindowSubclassHandler(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == (uint)NativeMethods.WM_TRAYICON)
        {
            int mouseMsg = unchecked((int)lParam.ToInt64());
            if (mouseMsg == NativeMethods.WM_LBUTTONUP || mouseMsg == NativeMethods.WM_LBUTTONDBLCLK)
            {
                ShowWindowFromTray();
                return IntPtr.Zero;
            }
            else if (mouseMsg == NativeMethods.WM_RBUTTONUP || mouseMsg == NativeMethods.WM_CONTEXTMENU)
            {
                ShowTrayContextMenu();
                return IntPtr.Zero;
            }
        }
        else if (uMsg == (uint)NativeMethods.WM_HOTKEY)
        {
            if ((int)wParam == 9001)
            {
                NativeMethods.AllowSetForegroundWindow(Environment.ProcessId);
                NativeMethods.LockSetForegroundWindow(NativeMethods.LSFW_UNLOCK);
                _screensaverManager.StartScreensaver();
                return IntPtr.Zero;
            }
        }
        else if (NativeMethods.WM_SHOWME != 0 && uMsg == NativeMethods.WM_SHOWME)
        {
            App.Log("WindowSubclassHandler received WM_SHOWME. Restoring MainWindow.");
            ShowWindowFromTray();
            return IntPtr.Zero;
        }
        else if (uMsg == (uint)NativeMethods.WM_SETTINGCHANGE || uMsg == (uint)NativeMethods.WM_THEMECHANGED)
        {
            if (_settingsService.Current.ThemePreference == AppThemeMode.System)
            {
                DispatcherQueue.TryEnqueue(() => ApplyThemePreference());
            }
        }
        else if (uMsg == (uint)NativeMethods.WM_DISPLAYCHANGE)
        {
            App.Log("WindowSubclassHandler received WM_DISPLAYCHANGE.");
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_screensaverManager.IsActive)
                {
                    App.Log("WM_DISPLAYCHANGE: Dismissing screensaver due to display topology change.");
                    _screensaverManager.StopScreensaver();
                }
                MainView?.UpdateScreenInfo();
            });
        }
        else if (uMsg == (uint)NativeMethods.WM_DESTROY)
        {
            if (_subclassProc != null)
            {
                NativeMethods.RemoveWindowSubclass(hWnd, _subclassProc, uIdSubclass);
            }
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ApplyThemePreference()
    {
        try
        {
            var pref = _settingsService?.Current.ThemePreference ?? AppThemeMode.System;
            bool isDark;

            switch (pref)
            {
                case AppThemeMode.Dark:
                    isDark = true;
                    break;
                case AppThemeMode.Light:
                    isDark = false;
                    break;
                case AppThemeMode.System:
                default:
                    isDark = NativeMethods.IsWindowsInDarkMode();
                    break;
            }

            // Apply theme to WinUI XAML visual tree
            if (Content is FrameworkElement root)
            {
                root.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
            }

            // Apply theme to native Win32 window (DWM immersive dark mode for titlebar/frame)
            var hWnd = WindowNative.GetWindowHandle(this);
            if (hWnd != IntPtr.Zero)
            {
                int darkVal = isDark ? 1 : 0;
                NativeMethods.DwmSetWindowAttribute(hWnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));

                // Apply to Win32 UXTheme for popup menus (2 = ForceDark, 3 = ForceLight, 1 = AllowDark)
                int appMode = isDark ? 2 : 3;
                NativeMethods.SetPreferredAppMode(appMode);
                NativeMethods.AllowDarkModeForWindow(hWnd, isDark);
                NativeMethods.FlushMenuThemes();
            }

            App.Log($"ApplyThemePreference: Mode={pref}, IsDark={isDark}");
        }
        catch (Exception ex)
        {
            App.Log($"ApplyThemePreference error: {ex.Message}");
        }
    }

    private void CenterWindowOnPrimary()
    {
        try
        {
            var primary = DisplayArea.Primary;
            if (primary != null)
            {
                var workArea = primary.WorkArea;
                var x = workArea.X + (workArea.Width - 1000) / 2;
                var y = workArea.Y + (workArea.Height - 660) / 2;
                AppWindow.Move(new PointInt32(x, y));
            }
        }
        catch { }
    }

    private void ShowWindowFromTray()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var hWnd = WindowNative.GetWindowHandle(this);

                CenterWindowOnPrimary();
                AppWindow.IsShownInSwitchers = true;
                AppWindow.Show();
                NativeMethods.ShowWindow(hWnd, 9); // SW_RESTORE
                NativeMethods.SetForegroundWindow(hWnd);

                // Restore opacity to 100% visible
                NativeMethods.SetLayeredWindowAttributes(hWnd, 0, 255, NativeMethods.LWA_ALPHA);

                // Re-enable DWM transitions once restored
                int forceDisable = 0;
                NativeMethods.DwmSetWindowAttribute(hWnd, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, ref forceDisable, sizeof(int));
            }
            catch (Exception ex)
            {
                App.Log($"ShowWindowFromTray error: {ex.Message}");
            }
        });
    }

    private void ShowTrayContextMenu()
    {
        ApplyThemePreference();

        var hWnd = WindowNative.GetWindowHandle(this);
        if (!NativeMethods.GetCursorPos(out var pt))
            return;

        IntPtr hMenu = NativeMethods.CreatePopupMenu();
        if (hMenu == IntPtr.Zero)
            return;

        try
        {
            NativeMethods.AppendMenuW(hMenu, NativeMethods.MF_STRING, CMD_TRAY_OPEN, "Open LivelyScreen");
            NativeMethods.AppendMenuW(hMenu, NativeMethods.MF_STRING, CMD_TRAY_START, "Start Screensaver Now");
            NativeMethods.AppendMenuW(hMenu, NativeMethods.MF_STRING, CMD_TRAY_NEXT, "Next Wallpaper / Media");
            NativeMethods.AppendMenuW(hMenu, NativeMethods.MF_SEPARATOR, 0, string.Empty);
            NativeMethods.AppendMenuW(hMenu, NativeMethods.MF_STRING, CMD_TRAY_EXIT, "Exit LivelyScreen");

            NativeMethods.SetMenuDefaultItem(hMenu, CMD_TRAY_OPEN, false);

            NativeMethods.SetForegroundWindow(hWnd);

            uint cmd = NativeMethods.TrackPopupMenuEx(
                hMenu,
                NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON,
                pt.X,
                pt.Y,
                hWnd,
                IntPtr.Zero
            );

            NativeMethods.PostMessage(hWnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (cmd == CMD_TRAY_OPEN)
            {
                ShowWindowFromTray();
            }
            else if (cmd == CMD_TRAY_START)
            {
                DispatcherQueue.TryEnqueue(() => _screensaverManager.StartScreensaver());
            }
            else if (cmd == CMD_TRAY_NEXT)
            {
                DispatcherQueue.TryEnqueue(() => _screensaverManager.AdvanceNextMedia());
            }
            else if (cmd == CMD_TRAY_EXIT)
            {
                ExitApplication();
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(hMenu);
        }
    }

    private void ExitApplication()
    {
        App.Log("ExitApplication called from tray menu");
        CleanupTrayAndHooks();
        Environment.Exit(0);
    }

    private void CleanupTrayAndHooks()
    {
        try
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _nid);
            if (_hBalloonIcon != IntPtr.Zero && _hBalloonIcon != _hTrayIcon)
            {
                NativeMethods.DestroyIcon(_hBalloonIcon);
                _hBalloonIcon = IntPtr.Zero;
            }
            if (_hTrayIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_hTrayIcon);
                _hTrayIcon = IntPtr.Zero;
            }

            var hWnd = WindowNative.GetWindowHandle(this);
            NativeMethods.UnregisterHotKey(hWnd, 9001);
            if (_subclassProc != null)
            {
                NativeMethods.RemoveWindowSubclass(hWnd, _subclassProc, (UIntPtr)1001);
                _subclassProc = null;
            }
            _idleDetector.Stop();
            _screensaverManager.StopScreensaver();
        }
        catch (Exception ex)
        {
            App.Log($"CleanupTrayAndHooks error: {ex.Message}");
        }
    }

    public void ShowNotification(string title, string message)
    {
        try
        {
            ToastNotificationService.ShowToastWithBanner(title, message);
        }
        catch (Exception ex)
        {
            App.Log($"ShowNotification Toast error: {ex.Message}");
            try
            {
                _nid.uFlags = NativeMethods.NIF_INFO;
                _nid.szInfoTitle = title;
                _nid.szInfo = message;
                _nid.dwInfoFlags = NativeMethods.NIIF_USER | NativeMethods.NIIF_LARGE_ICON;
                _nid.hBalloonIcon = _hBalloonIcon != IntPtr.Zero ? _hBalloonIcon : _hTrayIcon;
                NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _nid);
            }
            catch { }
        }
    }

    private string? _currentHotkey;

    public bool RegisterGlobalHotkey(string? hotkeyStr = null)
    {
        try
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            NativeMethods.UnregisterHotKey(hWnd, 9001);

            hotkeyStr ??= _settingsService.Current.Hotkey;
            _currentHotkey = hotkeyStr;

            if (string.IsNullOrWhiteSpace(hotkeyStr) || hotkeyStr.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                App.Log("Global hotkey disabled");
                return true;
            }

            if (!ParseHotkey(hotkeyStr, out uint fsModifiers, out uint vk))
            {
                App.Log($"Global hotkey could not parse: '{hotkeyStr}'");
                return false;
            }

            bool success = NativeMethods.RegisterHotKey(
                hWnd,
                9001,
                fsModifiers | NativeMethods.MOD_NOREPEAT,
                vk
            );

            if (success)
            {
                App.Log($"Global hotkey registered successfully: '{hotkeyStr}' (modifiers=0x{fsModifiers:X4}, vk=0x{vk:X2})");
                return true;
            }
            else
            {
                App.Log($"Failed to register global hotkey '{hotkeyStr}'. Conflict: another app or Windows is already using it.");
                // If legacy Win+Ctrl+S failed, auto-fallback to safe default Ctrl+Alt+S
                if (hotkeyStr.Equals("Win+Ctrl+S", StringComparison.OrdinalIgnoreCase))
                {
                    App.Log("Attempting auto-fallback to Ctrl+Alt+S...");
                    _settingsService.Current.Hotkey = "Ctrl+Alt+S";
                    _settingsService.Save();
                    return RegisterGlobalHotkey("Ctrl+Alt+S");
                }
                return false;
            }
        }
        catch (Exception ex)
        {
            App.Log($"Failed to register hotkey: {ex.Message}");
            return false;
        }
    }

    public static bool ParseHotkey(string hotkeyStr, out uint fsModifiers, out uint vk)
    {
        fsModifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(hotkeyStr)) return false;

        var tokens = hotkeyStr.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var rawToken in tokens)
        {
            var token = rawToken.ToUpperInvariant();
            if (token is "CTRL" or "CONTROL")
            {
                fsModifiers |= NativeMethods.MOD_CONTROL;
            }
            else if (token is "ALT" or "MENU")
            {
                fsModifiers |= NativeMethods.MOD_ALT;
            }
            else if (token is "SHIFT")
            {
                fsModifiers |= NativeMethods.MOD_SHIFT;
            }
            else if (token is "WIN" or "WINDOWS")
            {
                fsModifiers |= NativeMethods.MOD_WIN;
            }
            else
            {
                if (token.Length == 1 && token[0] >= 'A' && token[0] <= 'Z')
                {
                    vk = token[0];
                }
                else if (token.Length == 1 && token[0] >= '0' && token[0] <= '9')
                {
                    vk = token[0];
                }
                else if (token.StartsWith('F') && int.TryParse(token[1..], out int fNum) && fNum >= 1 && fNum <= 24)
                {
                    vk = (uint)(0x70 + fNum - 1);
                }
                else if (token == "SPACE")
                {
                    vk = 0x20;
                }
                else if (Enum.TryParse<Windows.System.VirtualKey>(rawToken, true, out var parsedVk))
                {
                    vk = (uint)parsedVk;
                }
            }
        }

        return vk != 0;
    }
}
