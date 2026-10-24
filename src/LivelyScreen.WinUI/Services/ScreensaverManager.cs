using Windows.Graphics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using LivelyScreen.Models;
using LivelyScreen.Native;
using LivelyScreen_WinUI.Views;

namespace LivelyScreen.Services;

public class ScreensaverManager
{
    private readonly SettingsService _settingsService;
    private readonly MediaLibrary _mediaLibrary;
    private readonly IdleDetector _idleDetector;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly List<ScreensaverWindow> _activeWindows = new();
    private readonly object _lock = new();

    public bool IsActive => _activeWindows.Count > 0;

    public event Action? ScreensaverStarted;
    public event Action? ScreensaverStopped;

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
                    var displays = DisplayArea.FindAll();
                    var targetIdx = _settingsService.Current.TargetDisplayIndex;
                    var mode = _settingsService.Current.DisplayMode;
                    bool isClone = mode == MonitorMode.Clone;
                    bool isSpan = mode == MonitorMode.Span && targetIdx == -1 && displays.Count > 1;
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
                            foreach (var d in displays)
                            {
                                minX = Math.Min(minX, d.OuterBounds.X);
                                minY = Math.Min(minY, d.OuterBounds.Y);
                                maxX = Math.Max(maxX, d.OuterBounds.X + d.OuterBounds.Width);
                                maxY = Math.Max(maxY, d.OuterBounds.Y + d.OuterBounds.Height);
                            }
                            vx = minX; vy = minY; vw = maxX - minX; vh = maxY - minY;
                        }

                        var spanBounds = new RectInt32(vx, vy, vw, vh);
                        LivelyScreen_WinUI.App.Log($"Creating SPAN screensaver window: {spanBounds.Width}x{spanBounds.Height} at ({spanBounds.X},{spanBounds.Y})");
                        var win = new ScreensaverWindow(_settingsService, _mediaLibrary, spanBounds, isBlackout: false, isMaster: true, isPrimary: true, isSpan: true, displayIndex: 0, assignedSourcePath: _settingsService.Current.ActiveSourcePath);
                        win.DismissRequested += StopScreensaver;
                        _activeWindows.Add(win);
                        win.Activate();
                    }
                    else if (displays.Count == 0)
                    {
                        var primary = DisplayArea.Primary;
                        var bounds = primary != null ? primary.OuterBounds : new RectInt32(0, 0, 1920, 1080);
                        LivelyScreen_WinUI.App.Log($"Creating primary screensaver window: {bounds.Width}x{bounds.Height}");
                        var win = new ScreensaverWindow(_settingsService, _mediaLibrary, bounds, isBlackout: false, isMaster: true, isPrimary: true, isSpan: false, displayIndex: 0, assignedSourcePath: _settingsService.Current.GetSourceForDisplay(0));
                        win.DismissRequested += StopScreensaver;
                        _activeWindows.Add(win);
                        win.Activate();
                    }
                    else
                    {
                        for (int i = 0; i < displays.Count; i++)
                        {
                            var display = displays[i];
                            var bounds = display.OuterBounds;
                            bool isBlackout = (targetIdx >= 0 && targetIdx != i);
                            bool isMaster = !isClone || (masterWin == null && !isBlackout);
                            string displaySource = _settingsService.Current.GetSourceForDisplay(i);

                            LivelyScreen_WinUI.App.Log($"Creating screensaver window for display {i}: {bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y}) (Blackout={isBlackout}, Master={isMaster}, Primary={display.IsPrimary}, Source={displaySource})");
                            var win = new ScreensaverWindow(_settingsService, _mediaLibrary, bounds, isBlackout: isBlackout, isMaster: isMaster, isPrimary: display.IsPrimary, isSpan: false, displayIndex: i, assignedSourcePath: displaySource);
                            win.DismissRequested += StopScreensaver;
                            _activeWindows.Add(win);

                            if (isClone && isMaster)
                            {
                                masterWin = win;
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
                            }
                        }

                        // Activate all windows, activating master window last for foreground ownership
                        foreach (var win in _activeWindows.OrderBy(w => w.IsMaster ? 1 : 0))
                        {
                            win.Activate();
                        }

                        // Ensure slave screens display the exact initial master item
                        if (isClone && masterWin != null && masterWin.CurrentMediaItem != null)
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

    public void StopScreensaver()
    {
        LivelyScreen_WinUI.App.Log("StopScreensaver called");
        _dispatcherQueue.TryEnqueue(() =>
        {
            lock (_lock)
            {
                if (!IsActive)
                    return;

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
