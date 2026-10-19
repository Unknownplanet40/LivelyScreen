using System.Runtime.InteropServices;

namespace LivelyScreen.Native;

public static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
        QUNS_APP = 7
    }

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE pquns);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int StrCmpLogicalW(string psz1, string psz2);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : (IntPtr)GetWindowLong32(hWnd, nIndex);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : (IntPtr)SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_POPUP = 0x80000000L;
    public const long WS_VISIBLE = 0x10000000L;
    public const long WS_CAPTION = 0x00C00000L;
    public const long WS_THICKFRAME = 0x00040000L;
    public const long WS_BORDER = 0x00800000L;
    public const long WS_DLGFRAME = 0x00400000L;
    public const long WS_EX_TOPMOST = 0x00000008L;
    public const long WS_EX_LAYERED = 0x00080000L;
    public const uint LWA_ALPHA = 0x00000002;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_TOP = new IntPtr(0);
    public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarInstance
    {
    }

    [ComImport]
    [Guid("602D4995-B13A-429B-A66E-1935E44F4317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList2
    {
        [PreserveSig]
        int HrInit();
        [PreserveSig]
        int AddTab(IntPtr hWnd);
        [PreserveSig]
        int DeleteTab(IntPtr hWnd);
        [PreserveSig]
        int ActivateTab(IntPtr hWnd);
        [PreserveSig]
        int SetActiveAlt(IntPtr hWnd);
        [PreserveSig]
        int MarkFullscreenWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
    }

    public static void MarkFullscreen(IntPtr hWnd, bool fullscreen)
    {
        if (hWnd == IntPtr.Zero) return;
        try
        {
            var taskbarList = (ITaskbarList2)new TaskbarInstance();
            taskbarList.HrInit();
            taskbarList.MarkFullscreenWindow(hWnd, fullscreen);
            Marshal.ReleaseComObject(taskbarList);
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"MarkFullscreen error: {ex.Message}");
        }
    }

    public static void ForceForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        IntPtr fgWnd = GetForegroundWindow();
        if (fgWnd == hWnd) return;

        uint fgThread = GetWindowThreadProcessId(fgWnd, IntPtr.Zero);
        uint curThread = GetCurrentThreadId();

        bool attached = false;
        try
        {
            if (fgThread != 0 && fgThread != curThread)
            {
                attached = AttachThreadInput(curThread, fgThread, true);
            }
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
        }
        catch { }
        finally
        {
            if (attached)
            {
                AttachThreadInput(curThread, fgThread, false);
            }
        }
    }

    public static void SuppressTaskbars(IntPtr screensaverHwnd)
    {
        if (screensaverHwnd == IntPtr.Zero) return;

        // Re-assert screensaver window at top of topmost Z-order
        SetWindowPos(screensaverHwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        // Put primary taskbar behind screensaver window
        IntPtr primaryTray = FindWindow("Shell_TrayWnd", null);
        if (primaryTray != IntPtr.Zero && primaryTray != screensaverHwnd)
        {
            SetWindowPos(primaryTray, screensaverHwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // Put secondary taskbars behind screensaver window
        IntPtr secTray = IntPtr.Zero;
        while ((secTray = FindWindowEx(IntPtr.Zero, secTray, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (secTray != screensaverHwnd)
            {
                SetWindowPos(secTray, screensaverHwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
    }

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        public MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;

    [Flags]
    public enum EXECUTION_STATE : uint
    {
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_SYSTEM_REQUIRED = 0x00000001
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus; // 0 = Offline (battery), 1 = Online (AC), 255 = Unknown
        public byte BatteryFlag;
        public byte BatteryLifePercent; // 0-100, 255 = Unknown
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NIF_INFO = 0x00000010;

    public const uint NIIF_NONE = 0x00000000;
    public const uint NIIF_INFO = 0x00000001;
    public const uint NIIF_WARNING = 0x00000002;
    public const uint NIIF_ERROR = 0x00000003;
    public const uint NIIF_USER = 0x00000004;
    public const uint NIIF_NOSOUND = 0x00000010;
    public const uint NIIF_LARGE_ICON = 0x00000020;
    public const uint NIIF_RESPECT_QUIET_TIME = 0x00000080;

    public const int WM_USER = 0x0400;
    public const int WM_TRAYICON = WM_USER + 101;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_DESTROY = 0x0002;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_THEMECHANGED = 0x031A;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_NULL = 0x0000;

    // --- UXTheme & DWM Dark Mode APIs ---
    public const int DWMWA_NCRENDERING_POLICY = 2;
    public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    public static extern int SetPreferredAppMode(int appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
    public static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

    [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
    public static extern void FlushMenuThemes();

    public static bool IsWindowsInDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var val = key.GetValue("AppsUseLightTheme");
                if (val is int intVal)
                {
                    return intVal == 0;
                }
            }
        }
        catch { }
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public const int HWND_BROADCAST = 0xffff;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern uint RegisterWindowMessage(string lpString);

    public static readonly uint WM_SHOWME = RegisterWindowMessage("LivelyScreen_ShowMainWindow_SingleInstance");

    public const uint TPM_LEFTALIGN = 0x0000;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint MF_ENABLED = 0x00000000;
    public const uint MF_GRAYED = 0x00000001;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lpTPMParams);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetMenuDefaultItem(IntPtr hMenu, uint uItem, bool fByPos);

    // Subclassing via comctl32.dll
    public delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    public static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);

    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x00000010;
    public const uint LR_DEFAULTSIZE = 0x00000040;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconFromResourceEx(
        byte[] pbIconBits,
        uint cbIconBits,
        bool fIcon,
        uint dwVersion,
        int cxDesired,
        int cyDesired,
        uint uFlags
    );

    /// <summary>
    /// Loads an icon from an .ico or .png file, automatically converting PNG to HICON with alpha transparency.
    /// </summary>
    public static IntPtr LoadIconFromFile(string filePath, int width, int height)
    {
        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            return IntPtr.Zero;

        try
        {
            if (filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = System.IO.File.ReadAllBytes(filePath);
                return CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x00030000, width, height, 0);
            }

            return LoadImage(IntPtr.Zero, filePath, IMAGE_ICON, width, height, LR_LOADFROMFILE);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    // --- Native Win32 File & Folder Dialogs (works in unpackaged & elevated WinUI 3) ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAME lpofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileNameW(ref OPENFILENAME lpofn);

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_EXPLORER = 0x00080000;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;

    public static string? PickFile(IntPtr hwndOwner, string title, string filter)
    {
        const int maxFile = 4096;
        IntPtr fileBuffer = Marshal.AllocHGlobal(maxFile * 2);
        try
        {
            Marshal.WriteInt16(fileBuffer, 0);
            var ofn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = hwndOwner,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = maxFile,
                lpstrTitle = title,
                Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
            };

            if (GetOpenFileNameW(ref ofn))
            {
                return Marshal.PtrToStringUni(fileBuffer);
            }
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"PickFile error: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
        }
        return null;
    }

    public static string? PickSaveFile(IntPtr hwndOwner, string title, string filter, string defaultExt, string initialFileName)
    {
        const int maxFile = 4096;
        IntPtr fileBuffer = Marshal.AllocHGlobal(maxFile * 2);
        try
        {
            Marshal.Copy(new char[maxFile], 0, fileBuffer, maxFile);
            if (!string.IsNullOrEmpty(initialFileName))
            {
                var chars = initialFileName.ToCharArray();
                Marshal.Copy(chars, 0, fileBuffer, Math.Min(chars.Length, maxFile - 1));
            }

            var ofn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = hwndOwner,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = maxFile,
                lpstrTitle = title,
                lpstrDefExt = defaultExt,
                Flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR
            };

            if (GetSaveFileNameW(ref ofn))
            {
                return Marshal.PtrToStringUni(fileBuffer);
            }
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"PickSaveFile error: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
        }
        return null;
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW { }

    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolderW(ref BROWSEINFO lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDListW(IntPtr pidl, IntPtr pszPath);

    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    public static string? PickFolder(IntPtr hwndOwner, string title)
    {
        try
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
            dialog.SetTitle(title);

            int hr = dialog.Show(hwndOwner);
            if (hr == 0)
            {
                dialog.GetResult(out IShellItem item);
                item.GetDisplayName(SIGDN_FILESYSPATH, out string path);
                return path;
            }
            return null;
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"IFileOpenDialog fallback to SHBrowseForFolderW: {ex.Message}");
            IntPtr displayBuf = Marshal.AllocHGlobal(520);
            IntPtr pathBuf = Marshal.AllocHGlobal(520);
            try
            {
                var bi = new BROWSEINFO
                {
                    hwndOwner = hwndOwner,
                    pszDisplayName = displayBuf,
                    lpszTitle = title,
                    ulFlags = 0x00000041 // BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE
                };
                IntPtr pidl = SHBrowseForFolderW(ref bi);
                if (pidl != IntPtr.Zero)
                {
                    if (SHGetPathFromIDListW(pidl, pathBuf))
                    {
                        return Marshal.PtrToStringUni(pathBuf);
                    }
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(displayBuf);
                Marshal.FreeHGlobal(pathBuf);
            }
            return null;
        }
    }
}
