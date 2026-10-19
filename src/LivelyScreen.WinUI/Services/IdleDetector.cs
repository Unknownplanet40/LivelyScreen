using Microsoft.UI.Xaml;
using LivelyScreen.Models;
using LivelyScreen.Native;

namespace LivelyScreen.Services;

public class IdleDetector
{
    private readonly SettingsService _settingsService;
    private readonly DispatcherTimer _timer;
    private DateTime? _pauseUntil;
    private int _triggerCornerDwellCount = 0;
    private int _processCheckCounter = 0;
    private bool _isBlockedProcessRunning = false;

    public bool IsPaused => _pauseUntil.HasValue && DateTime.Now < _pauseUntil.Value;
    public bool IsScreensaverActive { get; set; } = false;

    public event Action? ScreensaverRequested;

    public IdleDetector(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    public void Pause(TimeSpan duration)
    {
        _pauseUntil = DateTime.Now.Add(duration);
    }

    public void Resume()
    {
        _pauseUntil = null;
    }

    private void OnTimerTick(object? sender, object e)
    {
        if (IsScreensaverActive || IsPaused)
            return;

        var s = _settingsService.Current;

        // 1. Hot Corners Check (Immediate evaluation on each second tick)
        int screenW = NativeMethods.GetSystemMetrics(0); // SM_CXSCREEN
        int screenH = NativeMethods.GetSystemMetrics(1); // SM_CYSCREEN
        const int cornerThreshold = 20;

        if (NativeMethods.GetCursorPos(out var pt) && screenW > 0 && screenH > 0)
        {
            var currentCorner = HotCornerZone.None;
            if (pt.X <= cornerThreshold && pt.Y <= cornerThreshold)
                currentCorner = HotCornerZone.TopLeft;
            else if (pt.X >= screenW - cornerThreshold && pt.Y <= cornerThreshold)
                currentCorner = HotCornerZone.TopRight;
            else if (pt.X <= cornerThreshold && pt.Y >= screenH - cornerThreshold)
                currentCorner = HotCornerZone.BottomLeft;
            else if (pt.X >= screenW - cornerThreshold && pt.Y >= screenH - cornerThreshold)
                currentCorner = HotCornerZone.BottomRight;

            // Inhibit activation if cursor is resting in the Lockout / Disable corner
            if (s.DisableCorner != HotCornerZone.None && currentCorner == s.DisableCorner)
            {
                _triggerCornerDwellCount = 0;
                return;
            }

            // Immediately launch screensaver if resting in Trigger corner for ~2s
            if (s.TriggerCorner != HotCornerZone.None && currentCorner == s.TriggerCorner)
            {
                _triggerCornerDwellCount++;
                if (_triggerCornerDwellCount >= 2)
                {
                    _triggerCornerDwellCount = 0;
                    ScreensaverRequested?.Invoke();
                    return;
                }
            }
            else
            {
                _triggerCornerDwellCount = 0;
            }
        }

        // 2. Battery Power Guard Check
        if (s.PauseOnBattery)
        {
            if (NativeMethods.GetSystemPowerStatus(out var powerStatus))
            {
                if (powerStatus.ACLineStatus == 0) // Offline = running on battery
                {
                    return;
                }
            }
        }

        // 3. Process Blocklist Check (sampled every 4s to avoid process list overhead)
        if (s.BlockedProcesses != null && s.BlockedProcesses.Count > 0)
        {
            _processCheckCounter++;
            if (_processCheckCounter >= 4)
            {
                _processCheckCounter = 0;
                _isBlockedProcessRunning = CheckBlockedProcessesRunning(s.BlockedProcesses);
            }
            if (_isBlockedProcessRunning)
            {
                return;
            }
        }
        else
        {
            _isBlockedProcessRunning = false;
        }

        // 4. Idle Duration Check
        var idleSeconds = GetIdleSeconds();
        var requiredIdleSeconds = s.IdleDelayMinutes * 60;

        if (idleSeconds >= requiredIdleSeconds)
        {
            if (s.SmartPauseInFullscreen && IsFullscreenOrGamingActive())
            {
                return;
            }

            ScreensaverRequested?.Invoke();
        }
    }

    private static bool CheckBlockedProcessesRunning(List<string> processNames)
    {
        try
        {
            var activeProcesses = System.Diagnostics.Process.GetProcesses();
            foreach (var p in activeProcesses)
            {
                try
                {
                    string pName = p.ProcessName;
                    foreach (var blocked in processNames)
                    {
                        if (string.IsNullOrWhiteSpace(blocked)) continue;
                        string cleanBlocked = blocked.Trim();
                        if (cleanBlocked.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            cleanBlocked = cleanBlocked[..^4];

                        if (string.Equals(pName, cleanBlocked, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch { }
        return false;
    }

    public static double GetIdleSeconds()
    {
        var lii = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.LASTINPUTINFO))
        };

        if (NativeMethods.GetLastInputInfo(ref lii))
        {
            var currentTick = (uint)Environment.TickCount;
            var elapsedTicks = currentTick - lii.dwTime;
            return elapsedTicks / 1000.0;
        }

        return 0;
    }

    public static bool IsFullscreenOrGamingActive()
    {
        try
        {
            var hr = NativeMethods.SHQueryUserNotificationState(out var state);
            if (hr == 0)
            {
                if (state == NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN ||
                    state == NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE ||
                    state == NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY)
                {
                    return true;
                }
            }
        }
        catch
        {
            // Fallback gracefully
        }

        return false;
    }
}
