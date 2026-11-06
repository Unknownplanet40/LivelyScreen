using Windows.Graphics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using LivelyScreen.Models;
using LivelyScreen.Native;
using LivelyScreen_WinUI.Controls;
using LivelyScreen_WinUI.Views;

namespace LivelyScreen.Services;

public class ScreensaverManager
{
    private readonly SettingsService _settingsService;
    private readonly MediaLibrary _mediaLibrary;
    private readonly IdleDetector _idleDetector;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly List<ScreensaverWindow> _activeWindows = new();
    private readonly DisplayPowerService _displayPowerService = new();
    private readonly object _lock = new();
    private DisplayAreaWatcher? _displayWatcher;
    private DispatcherQueueTimer? _displayReconcileTimer;

    public bool IsActive => _activeWindows.Count > 0;

    public event Action? ScreensaverStarted;
    public event Action? ScreensaverStopped;

    private readonly record struct MonitorDescriptor(
        int Index,
        ulong DisplayIdValue,
        RectInt32 Bounds,
        bool IsPrimary,
        DisplayArea? DisplayAreaRef);

    public ScreensaverManager(
        SettingsService settingsService,
        MediaLibrary mediaLibrary,
        IdleDetector idleDetector,
        DispatcherQueue dispatcherQueue)
    {
        _settingsService = settingsService;
        _mediaLibrary = mediaLibrary;
        _idleDetector = idleDetector;
        _dispatcherQueue = dispatcherQueue;

        _idleDetector.ScreensaverRequested += StartScreensaver;
        AppDomain.CurrentDomain.ProcessExit += (s, e) => _displayPowerService.WakeAllDisplays();
    }

    /// <summary>
    /// Enumerates all connected monitors and identifies the single primary monitor:
    /// 1) DisplayArea.IsPrimary == true (or matching DisplayArea.Primary.DisplayId),
    /// 2) Fallback: monitor whose OuterBounds origin is (0, 0),
    /// 3) Fallback: first enumerated monitor (index 0).
    /// </summary>
    private static List<MonitorDescriptor> GetConnectedMonitors()
    {
        var result = new List<MonitorDescriptor>();
        try
        {
            var displays = DisplayArea.FindAll();
            ulong? primaryId = null;
            try
            {
                primaryId = DisplayArea.Primary?.DisplayId.Value;
            }
            catch { }

            int primaryIndex = -1;
            for (int i = 0; i < displays.Count; i++)
            {
                var d = displays[i];
                if (d.IsPrimary || (primaryId.HasValue && d.DisplayId.Value == primaryId.Value))
                {
                    primaryIndex = i;
                    break;
                }
            }

            if (primaryIndex < 0)
            {
                for (int i = 0; i < displays.Count; i++)
                {
                    var b = displays[i].OuterBounds;
                    if (b.X == 0 && b.Y == 0)
                    {
                        primaryIndex = i;
                        break;
                    }
                }
            }

            if (primaryIndex < 0 && displays.Count > 0)
            {
                primaryIndex = 0;
            }

            for (int i = 0; i < displays.Count; i++)
            {
                var d = displays[i];
                var b = d.OuterBounds;
                if (b.Width <= 0 || b.Height <= 0) continue;
                result.Add(new MonitorDescriptor(
                    Index: i,
                    DisplayIdValue: d.DisplayId.Value,
                    Bounds: b,
                    IsPrimary: i == primaryIndex,
                    DisplayAreaRef: d));
            }
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"GetConnectedMonitors error: {ex.Message}");
        }

        if (result.Count == 0)
        {
            var primary = DisplayArea.Primary;
            var bounds = primary != null ? primary.OuterBounds : new RectInt32(0, 0, 1920, 1080);
            ulong id = primary?.DisplayId.Value ?? 1UL;
            result.Add(new MonitorDescriptor(0, id, bounds, IsPrimary: true, primary));
        }

        return result;
    }

    public void StartScreensaver()
    {
        LivelyScreen_WinUI.App.Log("StartScreensaver called");
        NativeMethods.AllowSetForegroundWindow(Environment.ProcessId);
        NativeMethods.LockSetForegroundWindow(NativeMethods.LSFW_UNLOCK);
        _dispatcherQueue.TryEnqueue(() =>
        {
            NativeMethods.AllowSetForegroundWindow(Environment.ProcessId);
            NativeMethods.LockSetForegroundWindow(NativeMethods.LSFW_UNLOCK);
            lock (_lock)
            {
                if (IsActive)
                {
                    LivelyScreen_WinUI.App.Log("Screensaver already active, ignoring");
                    return;
                }

                _idleDetector.IsScreensaverActive = true;
                _activeWindows.Clear();

                try
                {
                    var s = _settingsService.Current;
                    var globalSimMode = s.GetEffectiveSimulationMode();
                    bool isSimActive = globalSimMode != SimulationModeType.None;
                    if (isSimActive)
                    {
                        SimulationSuiteControl.BeginSharedSession(globalSimMode, s);
                    }

                    var monitors = GetConnectedMonitors();
                    var targetIdx = s.TargetDisplayIndex;
                    var mode = s.DisplayMode;
                    bool isClone = mode == MonitorMode.Clone;
                    bool isSpan = !isSimActive && mode == MonitorMode.Span && targetIdx == -1 && monitors.Count > 1;
                    ScreensaverWindow? masterWin = null;

                    if (isSpan)
                    {
                        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
                        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
                        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
                        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

                        if (vw <= 0 || vh <= 0)
                        {
                            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                            foreach (var m in monitors)
                            {
                                minX = Math.Min(minX, m.Bounds.X);
                                minY = Math.Min(minY, m.Bounds.Y);
                                maxX = Math.Max(maxX, m.Bounds.X + m.Bounds.Width);
                                maxY = Math.Max(maxY, m.Bounds.Y + m.Bounds.Height);
                            }
                            vx = minX; vy = minY; vw = maxX - minX; vh = maxY - minY;
                        }

                        var spanBounds = new RectInt32(vx, vy, vw, vh);
                        LivelyScreen_WinUI.App.Log($"Creating SPAN screensaver window: {spanBounds.Width}x{spanBounds.Height} at ({spanBounds.X},{spanBounds.Y})");
                        var win = new ScreensaverWindow(_settingsService, _mediaLibrary, spanBounds, isBlackout: false, isMaster: true, isPrimary: true, isSpan: true, displayIndex: 0, assignedSourcePath: s.ActiveSourcePath)
                        {
                            DisplayIdValue = 0UL
                        };
                        win.DismissRequested += StopScreensaver;
                        win.DisplayConfigurationChanged += ScheduleDisplayReconcile;
                        _activeWindows.Add(win);
                        win.Activate();
                    }
                    else
                    {
                        bool simMirror = isSimActive && string.Equals(
                            s.MULTI_MONITOR_MODE,
                            AppSettings.MultiMonitorModeMirror,
                            StringComparison.OrdinalIgnoreCase);

                        for (int i = 0; i < monitors.Count; i++)
                        {
                            var mon = monitors[i];
                            var bounds = mon.Bounds;
                            string displaySource = isSimActive
                                ? s.ActiveSourcePath
                                : s.GetSourceForDisplay(mon.Index);

                            bool isBlackout;
                            if (isSimActive)
                            {
                                // MULTI_MONITOR_MODE:
                                // - "primary_only" (default): primary monitor shows fake OS screen; all other monitors show solid black borderless window.
                                // - "mirror": every monitor shows the same synchronized fake OS screen, scaled to its own dimensions.
                                isBlackout = !simMirror && !mon.IsPrimary;
                            }
                            else
                            {
                                isBlackout = (targetIdx >= 0 && targetIdx != mon.Index);
                                if (isBlackout && s.PowerOffInactiveDisplays && mon.DisplayAreaRef != null)
                                {
                                    if (_displayPowerService.TryPowerOffDisplay(mon.DisplayAreaRef))
                                    {
                                        LivelyScreen_WinUI.App.Log($"Display {mon.Index} placed in DDC/CI standby, skipping blackout window.");
                                        continue;
                                    }
                                }
                            }

                            bool isMaster = isSimActive
                                ? mon.IsPrimary || (masterWin == null && !isBlackout)
                                : (!isClone || (masterWin == null && !isBlackout));

                            LivelyScreen_WinUI.App.Log($"Creating screensaver window for display {mon.Index}: {bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y}) (Blackout={isBlackout}, Master={isMaster}, Primary={mon.IsPrimary}, SimMode={globalSimMode}, MultiMonitorMode={s.MULTI_MONITOR_MODE})");
                            var win = new ScreensaverWindow(
                                _settingsService,
                                _mediaLibrary,
                                bounds,
                                isBlackout: isBlackout,
                                isMaster: isMaster,
                                isPrimary: mon.IsPrimary,
                                isSpan: false,
                                displayIndex: mon.Index,
                                assignedSourcePath: displaySource)
                            {
                                DisplayIdValue = mon.DisplayIdValue
                            };
                            win.DismissRequested += StopScreensaver;
                            win.DisplayConfigurationChanged += ScheduleDisplayReconcile;
                            _activeWindows.Add(win);

                            if (masterWin == null && isMaster)
                            {
                                masterWin = win;
                            }

                            if (!isSimActive && isClone && isMaster)
                            {
                                win.MediaBroadcastRequested += (item, initial) =>
                                {
                                    foreach (var other in _activeWindows)
                                    {
                                        if (other != win && !other.IsBlackout)
                                        {
                                            other.DisplayMedia(item, initial);
                                        }
                                    }
                                };
                                win.MediaPreloadRequested += (nextItem) =>
                                {
                                    foreach (var other in _activeWindows)
                                    {
                                        if (other != win && !other.IsBlackout)
                                        {
                                            other.PreloadMedia(nextItem);
                                        }
                                    }
                                };
                            }
                        }

                        // Activate all windows, activating master/primary window last for foreground ownership
                        foreach (var win in _activeWindows.OrderBy(w => w.IsMaster ? 1 : 0))
                        {
                            win.Activate();
                        }

                        // Ensure slave screens display the exact initial master item in standard Clone mode
                        if (!isSimActive && isClone && masterWin != null && masterWin.CurrentMediaItem != null)
                        {
                            foreach (var win in _activeWindows)
                            {
                                if (win != masterWin && !win.IsBlackout)
                                {
                                    win.DisplayMedia(masterWin.CurrentMediaItem, initial: true);
                                }
                            }
                        }
                    }

                    StartDisplayWatcher();

                    if (!_settingsService.Current.AllowDisplaySleep)
                    {
                        NativeMethods.SetThreadExecutionState(
                            NativeMethods.EXECUTION_STATE.ES_CONTINUOUS |
                            NativeMethods.EXECUTION_STATE.ES_DISPLAY_REQUIRED
                        );
                    }

                    ScreensaverStarted?.Invoke();
                }
                catch (Exception ex)
                {
                    LivelyScreen_WinUI.App.Log($"StartScreensaver EXCEPTION: {ex}");
                }
            }
        });
    }

    private void StartDisplayWatcher()
    {
        StopDisplayWatcher();
        try
        {
            _displayWatcher = DisplayArea.CreateWatcher();
            _displayWatcher.Added += OnDisplayAreaChanged;
            _displayWatcher.Removed += OnDisplayAreaChanged;
            _displayWatcher.Updated += OnDisplayAreaChanged;
            _displayWatcher.Start();
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"StartDisplayWatcher warning: {ex.Message}");
        }
    }

    private void StopDisplayWatcher()
    {
        try
        {
            if (_displayReconcileTimer != null)
            {
                _displayReconcileTimer.Stop();
                _displayReconcileTimer = null;
            }
            if (_displayWatcher != null)
            {
                _displayWatcher.Added -= OnDisplayAreaChanged;
                _displayWatcher.Removed -= OnDisplayAreaChanged;
                _displayWatcher.Updated -= OnDisplayAreaChanged;
                if (_displayWatcher.Status == DisplayAreaWatcherStatus.Started ||
                    _displayWatcher.Status == DisplayAreaWatcherStatus.EnumerationCompleted)
                {
                    _displayWatcher.Stop();
                }
                _displayWatcher = null;
            }
        }
        catch { }
    }

    private void OnDisplayAreaChanged(DisplayAreaWatcher sender, DisplayArea args)
    {
        ScheduleDisplayReconcile();
    }

    private void ScheduleDisplayReconcile()
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            lock (_lock)
            {
                if (!IsActive) return;

                if (_displayReconcileTimer == null)
                {
                    _displayReconcileTimer = _dispatcherQueue.CreateTimer();
                    _displayReconcileTimer.Interval = TimeSpan.FromMilliseconds(180);
                    _displayReconcileTimer.IsRepeating = false;
                    _displayReconcileTimer.Tick += (_, _) => ReconcileDisplays();
                }

                _displayReconcileTimer.Stop();
                _displayReconcileTimer.Start();
            }
        });
    }

    private void ReconcileDisplays()
    {
        lock (_lock)
        {
            if (!IsActive) return;

            try
            {
                var s = _settingsService.Current;
                var globalSimMode = s.GetEffectiveSimulationMode();
                bool isSimActive = globalSimMode != SimulationModeType.None;
                var monitors = GetConnectedMonitors();
                if (monitors.Count == 0) return;

                // If running a single span window (non-simulation mode), resize span window to updated virtual desktop
                if (!isSimActive && _activeWindows.Count == 1 && s.DisplayMode == MonitorMode.Span && s.TargetDisplayIndex == -1 && monitors.Count > 1)
                {
                    int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
                    int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
                    int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
                    int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
                    if (vw > 0 && vh > 0)
                    {
                        _activeWindows[0].ReconfigureMonitor(new RectInt32(vx, vy, vw, vh), isPrimary: true, isBlackout: false, isMaster: true, displayIndex: 0, assignedSourcePath: s.ActiveSourcePath);
                    }
                    return;
                }

                bool simMirror = isSimActive && string.Equals(
                    s.MULTI_MONITOR_MODE,
                    AppSettings.MultiMonitorModeMirror,
                    StringComparison.OrdinalIgnoreCase);

                var remainingWindows = new List<ScreensaverWindow>(_activeWindows);
                var updatedWindows = new List<ScreensaverWindow>();

                foreach (var mon in monitors)
                {
                    // Match existing window by DisplayIdValue first, then by matching bounds
                    var existing = remainingWindows.FirstOrDefault(w => w.DisplayIdValue != 0 && w.DisplayIdValue == mon.DisplayIdValue)
                                ?? remainingWindows.FirstOrDefault(w => w.MonitorBounds.X == mon.Bounds.X && w.MonitorBounds.Y == mon.Bounds.Y);

                    string displaySource = isSimActive
                        ? s.ActiveSourcePath
                        : s.GetSourceForDisplay(mon.Index);

                    bool isBlackout = isSimActive
                        ? (!simMirror && !mon.IsPrimary)
                        : (s.TargetDisplayIndex >= 0 && s.TargetDisplayIndex != mon.Index);

                    bool isMaster = mon.IsPrimary;

                    if (existing != null)
                    {
                        remainingWindows.Remove(existing);
                        existing.DisplayIdValue = mon.DisplayIdValue;
                        existing.ReconfigureMonitor(
                            mon.Bounds,
                            isPrimary: mon.IsPrimary,
                            isBlackout: isBlackout,
                            isMaster: isMaster,
                            displayIndex: mon.Index,
                            assignedSourcePath: displaySource);
                        updatedWindows.Add(existing);
                    }
                    else
                    {
                        LivelyScreen_WinUI.App.Log($"Hot-plugged monitor detected (index={mon.Index}, {mon.Bounds.Width}x{mon.Bounds.Height}): creating window (Blackout={isBlackout})");
                        var newWin = new ScreensaverWindow(
                            _settingsService,
                            _mediaLibrary,
                            mon.Bounds,
                            isBlackout: isBlackout,
                            isMaster: isMaster,
                            isPrimary: mon.IsPrimary,
                            isSpan: false,
                            displayIndex: mon.Index,
                            assignedSourcePath: displaySource)
                        {
                            DisplayIdValue = mon.DisplayIdValue
                        };
                        newWin.DismissRequested += StopScreensaver;
                        newWin.DisplayConfigurationChanged += ScheduleDisplayReconcile;
                        updatedWindows.Add(newWin);
                        newWin.Activate();
                    }
                }

                // Close any windows whose monitor was removed
                foreach (var removedWin in remainingWindows)
                {
                    LivelyScreen_WinUI.App.Log($"Monitor removed (index={removedWin.DisplayIndex}): closing window");
                    removedWin.CloseImmediately();
                }

                _activeWindows.Clear();
                _activeWindows.AddRange(updatedWindows);
            }
            catch (Exception ex)
            {
                LivelyScreen_WinUI.App.Log($"ReconcileDisplays error: {ex.Message}");
            }
        }
    }

    public void StopScreensaver()
    {
        LivelyScreen_WinUI.App.Log("StopScreensaver called");
        _dispatcherQueue.TryEnqueue(() =>
        {
            lock (_lock)
            {
                if (!IsActive)
                    return;

                StopDisplayWatcher();
                SimulationSuiteControl.EndSharedSession();
                ScreensaverWindow.SetCursorHidden(false);
                _displayPowerService.WakeAllDisplays();

                var windowsToClose = _activeWindows.ToList();
                _activeWindows.Clear();
                _idleDetector.IsScreensaverActive = false;
                NativeMethods.SetThreadExecutionState(NativeMethods.EXECUTION_STATE.ES_CONTINUOUS);

                int remaining = windowsToClose.Count;

                Action checkAllClosed = () =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        lock (_lock)
                        {
                            remaining--;
                            if (remaining <= 0)
                            {
                                ScreensaverStopped?.Invoke();
                                LivelyScreen_WinUI.App.Log("Screensaver fully stopped with synchronized fade out");
                            }
                        }
                    });
                };

                foreach (var window in windowsToClose)
                {
                    try
                    {
                        window.FadeOutAndClose(checkAllClosed);
                    }
                    catch (Exception ex)
                    {
                        LivelyScreen_WinUI.App.Log($"Error fading screensaver window: {ex.Message}");
                        checkAllClosed();
                    }
                }
            }
        });
    }

    public void AdvanceNextMedia()
    {
        LivelyScreen_WinUI.App.Log("AdvanceNextMedia called from tray");
        _dispatcherQueue.TryEnqueue(() =>
        {
            lock (_lock)
            {
                if (IsActive)
                {
                    foreach (var window in _activeWindows)
                    {
                        if (window.IsMaster || _settingsService.Current.DisplayMode != MonitorMode.Clone)
                        {
                            window.AdvanceToNextMedia(initial: false);
                        }
                    }
                }
                else
                {
                    // Advance sequence queue in library so next screensaver start plays the next item
                    _mediaLibrary.GetNextItem(_settingsService.Current.Order);
                }
            }
        });
    }
}
