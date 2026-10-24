using System.IO;
using Windows.Graphics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using LivelyScreen.Models;
using LivelyScreen.Native;
using LivelyScreen.Services;
using WinRT.Interop;

namespace LivelyScreen_WinUI.Views;

public sealed partial class ScreensaverWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly MediaLibrary _mediaLibrary;
    private readonly RectInt32 _bounds;
    private readonly bool _isBlackout;
    private readonly bool _isPrimary;
    private readonly bool _isSpan;

    private Windows.Foundation.Point? _initialMousePos;
    private bool _isClosing = false;
    private bool _isActivated = false;
    private bool _activeLayerIsA = true;
    private DateTime _activationTime = DateTime.MaxValue;
    private readonly DispatcherTimer _photoTimer;
    private readonly DispatcherTimer _videoWatchdogTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _clockShuffleTimer;
    private readonly DispatcherTimer _oledShiftTimer;
    private readonly DispatcherTimer _blackoutStageTimer;
    private readonly DispatcherTimer _taskbarWatchdogTimer;
    private IntPtr _windowHandle = IntPtr.Zero;
    private double _oledShiftAngle = 0;
    private bool _isSecondStageBlackout = false;
    private WidgetPosition _currentShufflePosition;
    private static readonly Random _rng = new();
    private Stream? _activeStreamA;
    private Stream? _activeStreamB;

    private Storyboard? _currentTransition;
    private Action? _pendingTransitionComplete;
    private Storyboard? _kenBurnsStoryboardA;
    private Storyboard? _kenBurnsStoryboardB;
    private NativeMethods.SubclassProc? _borderlessSubclassProc;

    public bool IsMaster { get; }
    public int DisplayIndex { get; }
    public bool IsBlackout => _isBlackout;
    public string? AssignedSourcePath { get; }
    public MediaItem? CurrentMediaItem { get; private set; }
    public event Action? DismissRequested;
    public event Action<MediaItem, bool>? MediaBroadcastRequested;

    public ScreensaverWindow(
        SettingsService settingsService,
        MediaLibrary mediaLibrary,
        RectInt32 bounds,
        bool isBlackout = false,
        bool isMaster = true,
        bool isPrimary = true,
        bool isSpan = false,
        int displayIndex = 0,
        string? assignedSourcePath = null)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _mediaLibrary = mediaLibrary;
        _bounds = bounds;
        _isBlackout = isBlackout;
        _isPrimary = isPrimary;
        _isSpan = isSpan;
        IsMaster = isMaster;
        DisplayIndex = displayIndex;
        AssignedSourcePath = assignedSourcePath;

        _photoTimer = new DispatcherTimer();
        _photoTimer.Tick += (_, _) =>
        {
            if (_isClosing || _isBlackout)
            {
                _photoTimer.Stop();
                return;
            }
            AdvanceToNextMedia();
        };

        _videoWatchdogTimer = new DispatcherTimer();
        _videoWatchdogTimer.Tick += (_, _) =>
        {
            _videoWatchdogTimer.Stop();
            if (_isClosing || _isBlackout) return;
            App.Log("Video watchdog triggered timeout: advancing to next media");
            AdvanceToNextMedia();
        };

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _clockTimer.Tick += (_, _) =>
        {
            if (_isClosing)
            {
                _clockTimer.Stop();
                return;
            }
            UpdateClockWidget();
        };

        _clockShuffleTimer = new DispatcherTimer();
        _clockShuffleTimer.Tick += OnClockShuffleTick;

        _oledShiftTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(50) };
        _oledShiftTimer.Tick += OnOledShiftTick;

        _blackoutStageTimer = new DispatcherTimer();
        _blackoutStageTimer.Tick += OnBlackoutStageTick;

        _taskbarWatchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _taskbarWatchdogTimer.Tick += (_, _) =>
        {
            if (!_isClosing && _windowHandle != IntPtr.Zero)
            {
                NativeMethods.SuppressTaskbars(_windowHandle);
            }
        };

        _currentShufflePosition = _settingsService.Current.ClockPosition;
        ApplyClockPosition(_currentShufflePosition);
        UpdateClockWidget();
        bool canShowWidgets = CanDisplayWidgets();
        if (canShowWidgets && !_isBlackout)
        {
            _clockTimer.Start();
            StartClockShuffleTimer();
            StartOledShiftTimer();
        }
        StartBlackoutStageTimer();

        try
        {
            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            AppWindow.SetPresenter(presenter);
            AppWindow.MoveAndResize(_bounds);
        }
        catch (Exception ex)
        {
            App.Log($"ScreensaverWindow ctor presenter init error: {ex.Message}");
        }

        Closed += (_, _) =>
        {
            _isClosing = true;
            DismissRequested = null;
            MediaBroadcastRequested = null;
            _photoTimer.Stop();
            _videoWatchdogTimer.Stop();
            _clockTimer.Stop();
            _clockShuffleTimer.Stop();
            _oledShiftTimer.Stop();
            _blackoutStageTimer.Stop();
            _taskbarWatchdogTimer.Stop();
            _kenBurnsStoryboardA?.Stop();
            _kenBurnsStoryboardB?.Stop();
            _currentTransition?.Stop();
            UnloadLayer(ImageA, VideoA, isLayerA: true);
            UnloadLayer(ImageB, VideoB, isLayerA: false);
        };

        Activated += OnWindowActivated;
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_isActivated) return;
        _isActivated = true;

        App.Log($"ScreensaverWindow OnWindowActivated on bounds {_bounds.Width}x{_bounds.Height} at ({_bounds.X},{_bounds.Y}) (Blackout={_isBlackout}, Master={IsMaster}, Span={_isSpan})");
        try
        {
            var presenter = AppWindow.Presenter as OverlappedPresenter ?? OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            AppWindow.SetPresenter(presenter);
            AppWindow.MoveAndResize(_bounds);

            var hWnd = WindowNative.GetWindowHandle(this);
            _windowHandle = hWnd;
            if (hWnd != IntPtr.Zero)
            {
                // Force pure borderless POPUP style on Win32 window level
                long style = (long)NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE);
                style &= ~(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME | NativeMethods.WS_BORDER | NativeMethods.WS_DLGFRAME);
                style |= NativeMethods.WS_POPUP | NativeMethods.WS_VISIBLE;
                NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE, (IntPtr)style);

                long exStyle = (long)NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
                exStyle &= ~(0x00000100L /* WS_EX_WINDOWEDGE */ | 0x00000200L /* WS_EX_CLIENTEDGE */ | 0x00020000L /* WS_EX_STATICEDGE */ | 0x00000001L /* WS_EX_DLGMODALFRAME */);
                exStyle |= NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_LAYERED | 0x00000080L /* WS_EX_TOOLWINDOW */;
                NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE, (IntPtr)exStyle);

                // Disable Windows 11 rounded corners and accent border
                int doNotRound = 1; // DWMWCP_DONOTROUND
                NativeMethods.DwmSetWindowAttribute(hWnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref doNotRound, sizeof(int));

                // Explicitly disable DWM border rendering using DWMWA_COLOR_NONE (0xFFFFFFFE)
                uint noBorderColor = NativeMethods.DWMWA_COLOR_NONE;
                NativeMethods.DwmSetWindowAttribute(hWnd, NativeMethods.DWMWA_BORDER_COLOR, ref noBorderColor, sizeof(uint));

                int zeroThickness = 0; // DWMWA_VISIBLE_FRAME_BORDER_THICKNESS
                NativeMethods.DwmSetWindowAttribute(hWnd, 37, ref zeroThickness, sizeof(int));

                int ncpDisabled = 1; // DWMNCRP_DISABLED
                NativeMethods.DwmSetWindowAttribute(hWnd, NativeMethods.DWMWA_NCRENDERING_POLICY, ref ncpDisabled, sizeof(int));

                NativeMethods.SetLayeredWindowAttributes(hWnd, 0, 0, NativeMethods.LWA_ALPHA);

                // Universal 2px overscan pushes any potential 1px DWM window border completely off the physical screen
                const int overscan = 2;
                int x = _bounds.X - overscan;
                int y = _bounds.Y - overscan;
                int w = _bounds.Width + (overscan * 2);
                int h = _bounds.Height + (overscan * 2);

                if (_isSpan)
                {
                    const int spanExtra = 2;
                    x -= spanExtra;
                    y -= spanExtra;
                    w += spanExtra * 2;
                    h += spanExtra * 2;
                }

                // Set exact display bounds and bring to topmost
                NativeMethods.SetWindowPos(
                    hWnd,
                    NativeMethods.HWND_TOPMOST,
                    x,
                    y,
                    w,
                    h,
                    NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW
                );

                // Subclass window procedure to intercept WM_NCCALCSIZE and WM_NCACTIVATE so OS never draws non-client frame
                _borderlessSubclassProc = BorderlessSubclass;
                NativeMethods.SetWindowSubclass(hWnd, _borderlessSubclassProc, (UIntPtr)101, UIntPtr.Zero);

                // Set explicit GDI rectangular region which forces DWM to completely drop all borders and drop shadows
                IntPtr hRgn = NativeMethods.CreateRectRgn(0, 0, w, h);
                if (hRgn != IntPtr.Zero)
                {
                    NativeMethods.SetWindowRgn(hWnd, hRgn, true);
                }

                // Tell Windows Shell to treat window as fullscreen to suppress taskbar
                NativeMethods.MarkFullscreen(hWnd, true);

                if (IsMaster || _isPrimary)
                {
                    NativeMethods.ForceForegroundWindow(hWnd);
                    EventCapturer?.Focus(FocusState.Programmatic);
                }

                NativeMethods.SuppressTaskbars(hWnd);
                _taskbarWatchdogTimer.Start();
            }

            _activationTime = DateTime.UtcNow;

            ApplyVisualFilters();
            if (IsMaster && !_isBlackout)
            {
                AmbientAudioService.Start(_settingsService.Current);
            }
            StartFadeIn(hWnd);

            if (_isBlackout)
            {
                NoMediaPrompt.Visibility = Visibility.Collapsed;
                LayerA.Visibility = Visibility.Collapsed;
                LayerB.Visibility = Visibility.Collapsed;
            }
            else if (IsMaster || _settingsService.Current.DisplayMode != MonitorMode.Clone)
            {
                AdvanceToNextMedia(initial: true);
            }
        }
        catch (Exception ex)
        {
            App.Log($"OnWindowActivated EXCEPTION: {ex}");
        }
    }

    private IntPtr BorderlessSubclass(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        const uint WM_ACTIVATE = 0x0006;
        const uint WM_NCCALCSIZE = 0x0083;
        const uint WM_NCPAINT = 0x0085;
        const uint WM_NCACTIVATE = 0x0086;

        switch (uMsg)
        {
            case WM_ACTIVATE:
                if (wParam == IntPtr.Zero && !_isClosing)
                {
                    // Focus lost to taskbar or background window; ensure taskbar remains behind screensaver
                    NativeMethods.SuppressTaskbars(hWnd);
                }
                break;

            case (uint)NativeMethods.WM_DISPLAYCHANGE:
                TriggerDismiss();
                break;

            case WM_NCCALCSIZE:
                // Returning 0 indicates client area occupies 100% of the window rectangle, leaving 0 non-client pixels
                return IntPtr.Zero;

            case WM_NCACTIVATE:
                // Returning 1 prevents Windows DWM from painting active or inactive frame/border
                return (IntPtr)1;

            case WM_NCPAINT:
                // Suppress non-client border painting
                return IntPtr.Zero;
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void StartFadeIn(IntPtr hWnd)
    {
        var startTime = DateTime.UtcNow;
        const double durationMs = 600.0;
        var s = _settingsService.Current;
        double targetVolume = (s.VideoAudioEnabled ? s.VideoVolumePercent : 0) / 100.0;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (snd, _) =>
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            double progress = Math.Clamp(elapsed / durationMs, 0.0, 1.0);
            double eased = 1.0 - Math.Pow(1.0 - progress, 3); // Cubic ease-out

            byte alpha = (byte)(eased * 255);
            if (hWnd != IntPtr.Zero)
            {
                NativeMethods.SetLayeredWindowAttributes(hWnd, 0, alpha, NativeMethods.LWA_ALPHA);
            }
            if (RootWindowGrid != null)
            {
                RootWindowGrid.Opacity = eased;
            }

            // Smooth audio ramp up
            if (targetVolume > 0)
            {
                var curVid = _activeLayerIsA ? VideoA : VideoB;
                if (curVid.MediaPlayer != null)
                {
                    curVid.MediaPlayer.Volume = eased * targetVolume;
                }
            }

            if (progress >= 1.0)
            {
                timer.Stop();
                if (hWnd != IntPtr.Zero)
                {
                    NativeMethods.SetLayeredWindowAttributes(hWnd, 0, 255, NativeMethods.LWA_ALPHA);
                }
                if (RootWindowGrid != null) RootWindowGrid.Opacity = 1.0;
            }
        };
        timer.Start();
    }

    public void DisplayMedia(MediaItem item, bool initial = false)
    {
        if (_isClosing || _isBlackout) return;

        var profile = _settingsService.Current.GetProfileForPath(item.FilePath);

        if (CurrentMediaItem != null &&
            string.Equals(CurrentMediaItem.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            if (item.Type == MediaType.Image)
            {
                if (IsMaster || _settingsService.Current.DisplayMode != MonitorMode.Clone)
                {
                    _photoTimer.Interval = TimeSpan.FromSeconds(Math.Max(2, profile.PhotoDisplaySeconds));
                    _photoTimer.Start();
                }
                return;
            }
            else if (item.Type == MediaType.Video)
            {
                var curVid = _activeLayerIsA ? VideoA : VideoB;
                if (curVid.MediaPlayer != null)
                {
                    try
                    {
                        curVid.MediaPlayer.PlaybackSession.Position = TimeSpan.Zero;
                        curVid.MediaPlayer.Play();
                        return;
                    }
                    catch (Exception ex)
                    {
                        App.Log($"Error rewinding video: {ex.Message}");
                    }
                }
            }
        }

        _photoTimer.Stop();
        _videoWatchdogTimer.Stop();
        CurrentMediaItem = item;
        NoMediaPrompt.Visibility = Visibility.Collapsed;
        App.Log($"DisplayMedia on {(_bounds.X == 0 && _bounds.Y == 0 ? "primary" : "secondary")}: {item.FileName} (initial={initial}, isMaster={IsMaster})");

        if (initial)
        {
            LoadMediaIntoLayer(LayerA, ImageA, VideoA, item, isLayerA: true);
            LayerA.Opacity = 1;
            LayerB.Opacity = 0;
            _activeLayerIsA = true;
        }
        else
        {
            if (_activeLayerIsA)
            {
                LoadMediaIntoLayer(LayerB, ImageB, VideoB, item, isLayerA: false);
                ExecuteTransition(LayerA, LayerB, profile, () =>
                {
                    UnloadLayer(ImageA, VideoA, isLayerA: true);
                    _activeLayerIsA = false;
                });
            }
            else
            {
                LoadMediaIntoLayer(LayerA, ImageA, VideoA, item, isLayerA: true);
                ExecuteTransition(LayerB, LayerA, profile, () =>
                {
                    UnloadLayer(ImageB, VideoB, isLayerA: false);
                    _activeLayerIsA = true;
                });
            }
        }

        UpdateMediaInfoPill(item);
        if (_windowHandle != IntPtr.Zero && !_isClosing)
        {
            NativeMethods.SuppressTaskbars(_windowHandle);
        }
    }

    public void AdvanceToNextMedia(bool initial = false)
    {
        if (_isClosing || _isBlackout) return;

        if (!IsMaster && _settingsService.Current.DisplayMode == MonitorMode.Clone)
        {
            return;
        }

        var item = _mediaLibrary.GetNextItem(_settingsService.Current.Order, AssignedSourcePath);
        if (_isClosing || _isBlackout) return;

        if (item == null)
        {
            NoMediaPrompt.Visibility = Visibility.Visible;
            return;
        }

        DisplayMedia(item, initial);

        if (IsMaster && _settingsService.Current.DisplayMode == MonitorMode.Clone)
        {
            MediaBroadcastRequested?.Invoke(item, initial);
        }
    }

    private async void LoadMediaIntoLayer(
        Microsoft.UI.Xaml.Controls.Grid layer,
        Microsoft.UI.Xaml.Controls.Image imgCtrl,
        Microsoft.UI.Xaml.Controls.MediaPlayerElement vidCtrl,
        MediaItem item,
        bool isLayerA)
    {
        if (_isClosing || _isBlackout) return;

        var settings = _settingsService.Current;
        var profile = settings.GetProfileForPath(item.FilePath);
        var fitMode = profile.FitMode;
        var stretchMode = fitMode switch
        {
            MediaFitMode.Fit => Microsoft.UI.Xaml.Media.Stretch.Uniform,
            MediaFitMode.Stretch => Microsoft.UI.Xaml.Media.Stretch.Fill,
            MediaFitMode.Center => Microsoft.UI.Xaml.Media.Stretch.None,
            _ => Microsoft.UI.Xaml.Media.Stretch.UniformToFill
        };
        imgCtrl.Stretch = stretchMode;
        vidCtrl.Stretch = stretchMode;

        if (item.Type == MediaType.Video)
        {
            imgCtrl.Visibility = Visibility.Collapsed;
            imgCtrl.Source = null;

            vidCtrl.Visibility = Visibility.Visible;
            try
            {
                if (!File.Exists(item.FilePath))
                {
                    App.Log($"Video file not found: {item.FilePath}");
                    if (!_isClosing && !_isBlackout && (IsMaster || settings.DisplayMode != MonitorMode.Clone))
                    {
                        AdvanceToNextMedia();
                    }
                    return;
                }

                // Cleanly dispose existing player on this element first to avoid hardware decoder exhaustion
                var oldPlayer = vidCtrl.MediaPlayer;
                vidCtrl.SetMediaPlayer(null);
                try { oldPlayer?.Dispose(); } catch { }

                MediaSource mediaSource;
                try
                {
                    mediaSource = MediaSource.CreateFromUri(new Uri(item.FilePath));
                }
                catch
                {
                    var fs = File.OpenRead(item.FilePath);
                    if (isLayerA)
                    {
                        _activeStreamA?.Dispose();
                        _activeStreamA = fs;
                    }
                    else
                    {
                        _activeStreamB?.Dispose();
                        _activeStreamB = fs;
                    }

                    var ras = fs.AsRandomAccessStream();
                    mediaSource = MediaSource.CreateFromStream(ras, GetVideoMimeType(item.FilePath));
                }

                bool shouldMute = !IsMaster || !settings.VideoAudioEnabled;
                var player = new MediaPlayer
                {
                    Source = mediaSource,
                    IsMuted = shouldMute,
                    Volume = shouldMute ? 0 : settings.VideoVolumePercent / 100.0
                };

                if (IsMaster || settings.DisplayMode != MonitorMode.Clone)
                {
                    player.MediaEnded += (_, _) =>
                    {
                        if (_isClosing || _isBlackout) return;
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (!_isClosing && !_isBlackout) AdvanceToNextMedia();
                        });
                    };
                    player.MediaFailed += (_, args) =>
                    {
                        if (_isClosing || _isBlackout) return;
                        App.Log($"MediaPlayer failed on {item.FileName}: {args.Error} ({args.ErrorMessage})");
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (!_isClosing && !_isBlackout) AdvanceToNextMedia();
                        });
                    };
                    player.MediaOpened += (sender, _) =>
                    {
                        if (_isClosing || _isBlackout) return;
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (_isClosing || _isBlackout) return;
                            var dur = sender.NaturalDuration;
                            if (dur > TimeSpan.Zero && dur < TimeSpan.FromHours(6))
                            {
                                _videoWatchdogTimer.Interval = dur + TimeSpan.FromSeconds(3);
                            }
                            else
                            {
                                _videoWatchdogTimer.Interval = TimeSpan.FromMinutes(10);
                            }
                            _videoWatchdogTimer.Start();
                        });
                    };
                }

                vidCtrl.SetMediaPlayer(player);
                player.Play();
            }
            catch (Exception ex)
            {
                App.Log($"Error loading video {item.FileName}: {ex.Message}");
                if (!_isClosing && !_isBlackout && (IsMaster || settings.DisplayMode != MonitorMode.Clone))
                {
                    AdvanceToNextMedia();
                }
            }
        }
        else
        {
            var oldPlayer = vidCtrl.MediaPlayer;
            vidCtrl.SetMediaPlayer(null);
            try { oldPlayer?.Dispose(); } catch { }
            vidCtrl.Visibility = Visibility.Collapsed;

            imgCtrl.Visibility = Visibility.Visible;
            try
            {
                if (!File.Exists(item.FilePath))
                {
                    App.Log($"Image file not found: {item.FilePath}");
                    if (!_isClosing && !_isBlackout && (IsMaster || settings.DisplayMode != MonitorMode.Clone))
                    {
                        AdvanceToNextMedia();
                    }
                    return;
                }

                using var fs = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                var ras = fs.AsRandomAccessStream();

                int targetDecodeWidth = Math.Clamp(_bounds.Width > 0 ? _bounds.Width : 1920, 640, 2560);
                var bitmap = new BitmapImage
                {
                    DecodePixelWidth = targetDecodeWidth,
                    DecodePixelType = DecodePixelType.Physical
                };
                await bitmap.SetSourceAsync(ras);
                if (_isClosing || _isBlackout) return;
                imgCtrl.Source = bitmap;
            }
            catch (Exception ex)
            {
                App.Log($"Error loading image {item.FileName}: {ex.Message}");
                if (!_isClosing && !_isBlackout && (IsMaster || settings.DisplayMode != MonitorMode.Clone))
                {
                    AdvanceToNextMedia();
                    return;
                }
            }

            if (_isClosing || _isBlackout) return;

            if (profile.KenBurnsEffect)
            {
                var targetTransform = isLayerA ? ImageTransformA : ImageTransformB;
                targetTransform.ScaleX = 1.0;
                targetTransform.ScaleY = 1.0;
                targetTransform.TranslateX = 0;
                targetTransform.TranslateY = 0;

                var totalSec = profile.PhotoDisplaySeconds + profile.TransitionDurationSeconds;
                var kbDuration = new Duration(TimeSpan.FromSeconds(Math.Max(3.0, totalSec)));
                var kbEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

                var kbSb = new Storyboard();
                var scaleAnimX = new DoubleAnimation
                {
                    From = 1.0,
                    To = 1.08,
                    Duration = kbDuration,
                    EasingFunction = kbEase
                };
                Storyboard.SetTarget(scaleAnimX, targetTransform);
                Storyboard.SetTargetProperty(scaleAnimX, "ScaleX");
                kbSb.Children.Add(scaleAnimX);

                var scaleAnimY = new DoubleAnimation
                {
                    From = 1.0,
                    To = 1.08,
                    Duration = kbDuration,
                    EasingFunction = kbEase
                };
                Storyboard.SetTarget(scaleAnimY, targetTransform);
                Storyboard.SetTargetProperty(scaleAnimY, "ScaleY");
                kbSb.Children.Add(scaleAnimY);

                if (isLayerA)
                {
                    _kenBurnsStoryboardA?.Stop();
                    _kenBurnsStoryboardA = kbSb;
                }
                else
                {
                    _kenBurnsStoryboardB?.Stop();
                    _kenBurnsStoryboardB = kbSb;
                }
                kbSb.Begin();
            }

            if (_isClosing || _isBlackout) return;

            if (IsMaster || settings.DisplayMode != MonitorMode.Clone)
            {
                _photoTimer.Interval = TimeSpan.FromSeconds(Math.Max(2.0, profile.PhotoDisplaySeconds));
                _photoTimer.Start();
            }
        }
    }

    private void ExecuteTransition(
        Microsoft.UI.Xaml.Controls.Grid fromLayer,
        Microsoft.UI.Xaml.Controls.Grid toLayer,
        SourceProfile profile,
        Action onComplete)
    {
        if (_isClosing || _isBlackout) return;

        if (_currentTransition != null)
        {
            _currentTransition.Stop();
            _currentTransition = null;
            var prevAction = _pendingTransitionComplete;
            _pendingTransitionComplete = null;
            prevAction?.Invoke();
        }

        _pendingTransitionComplete = onComplete;
        var style = profile.TransitionStyle;
        var durationSec = Math.Max(0.2, profile.TransitionDurationSeconds);

        if (style == TransitionType.Cut)
        {
            toLayer.Opacity = 1;
            fromLayer.Opacity = 0;
            _pendingTransitionComplete = null;
            onComplete();
            return;
        }

        if (style == TransitionType.Crossfade)
        {
            var sb = new Storyboard();
            var duration = new Duration(TimeSpan.FromSeconds(durationSec));
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(fadeIn, toLayer);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            sb.Children.Add(fadeIn);

            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(fadeOut, fromLayer);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");
            sb.Children.Add(fadeOut);

            sb.Completed += (_, _) =>
            {
                fromLayer.Opacity = 0;
                toLayer.Opacity = 1;
                _currentTransition = null;
                var act = _pendingTransitionComplete;
                _pendingTransitionComplete = null;
                act?.Invoke();
            };

            _currentTransition = sb;
            toLayer.Opacity = 0;
            sb.Begin();
        }
        else if (style == TransitionType.FadeToBlack)
        {
            var halfSec = durationSec / 2.0;
            var halfDuration = new Duration(TimeSpan.FromSeconds(halfSec));
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

            var sbOut = new Storyboard();
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = halfDuration,
                EasingFunction = easeIn
            };
            Storyboard.SetTarget(fadeOut, fromLayer);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");
            sbOut.Children.Add(fadeOut);

            sbOut.Completed += (_, _) =>
            {
                fromLayer.Opacity = 0;
                var sbIn = new Storyboard();
                var fadeIn = new DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = halfDuration,
                    EasingFunction = easeOut
                };
                Storyboard.SetTarget(fadeIn, toLayer);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");
                sbIn.Children.Add(fadeIn);

                sbIn.Completed += (_, _) =>
                {
                    toLayer.Opacity = 1;
                    _currentTransition = null;
                    var act = _pendingTransitionComplete;
                    _pendingTransitionComplete = null;
                    act?.Invoke();
                };

                _currentTransition = sbIn;
                toLayer.Opacity = 0;
                sbIn.Begin();
            };

            _currentTransition = sbOut;
            toLayer.Opacity = 0;
            sbOut.Begin();
        }
        else if (style == TransitionType.Slide)
        {
            var duration = new Duration(TimeSpan.FromSeconds(durationSec));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            double width = _bounds.Width > 0 ? _bounds.Width : 1920;

            var fromTransform = fromLayer == LayerA ? TransformA : TransformB;
            var toTransform = toLayer == LayerA ? TransformA : TransformB;

            toLayer.Opacity = 1;
            toTransform.X = width;
            fromTransform.X = 0;

            var sb = new Storyboard();
            var animTo = new DoubleAnimation
            {
                From = width,
                To = 0.0,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(animTo, toTransform);
            Storyboard.SetTargetProperty(animTo, "X");
            sb.Children.Add(animTo);

            var animFrom = new DoubleAnimation
            {
                From = 0.0,
                To = -width,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(animFrom, fromTransform);
            Storyboard.SetTargetProperty(animFrom, "X");
            sb.Children.Add(animFrom);

            sb.Completed += (_, _) =>
            {
                fromLayer.Opacity = 0;
                fromTransform.X = 0;
                toTransform.X = 0;
                _currentTransition = null;
                var act = _pendingTransitionComplete;
                _pendingTransitionComplete = null;
                act?.Invoke();
            };

            _currentTransition = sb;
            sb.Begin();
        }
    }

    private void UnloadLayer(
        Microsoft.UI.Xaml.Controls.Image imgCtrl,
        Microsoft.UI.Xaml.Controls.MediaPlayerElement vidCtrl,
        bool isLayerA)
    {
        var oldPlayer = vidCtrl.MediaPlayer;
        vidCtrl.SetMediaPlayer(null);
        try
        {
            oldPlayer?.Dispose();
        }
        catch { }
        vidCtrl.Visibility = Visibility.Collapsed;

        imgCtrl.Source = null;
        imgCtrl.Visibility = Visibility.Collapsed;

        if (isLayerA)
        {
            _kenBurnsStoryboardA?.Stop();
            _kenBurnsStoryboardA = null;
            ImageTransformA.ScaleX = 1.0;
            ImageTransformA.ScaleY = 1.0;
            ImageTransformA.TranslateX = 0;
            ImageTransformA.TranslateY = 0;

            _activeStreamA?.Dispose();
            _activeStreamA = null;
        }
        else
        {
            _kenBurnsStoryboardB?.Stop();
            _kenBurnsStoryboardB = null;
            ImageTransformB.ScaleX = 1.0;
            ImageTransformB.ScaleY = 1.0;
            ImageTransformB.TranslateX = 0;
            ImageTransformB.TranslateY = 0;

            _activeStreamB?.Dispose();
            _activeStreamB = null;
        }
    }

    private static string GetVideoMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".avi" => "video/x-msvideo",
            _ => "video/mp4"
        };
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        // Ignore pointer movement for first 1.5 seconds to reject launch movement
        if ((DateTime.UtcNow - _activationTime).TotalSeconds < 1.5)
        {
            _initialMousePos = e.GetCurrentPoint(EventCapturer).Position;
            return;
        }

        var point = e.GetCurrentPoint(EventCapturer).Position;
        if (!_initialMousePos.HasValue)
        {
            _initialMousePos = point;
            return;
        }

        var dx = point.X - _initialMousePos.Value.X;
        var dy = point.Y - _initialMousePos.Value.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance >= _settingsService.Current.MouseSensitivityPixels)
        {
            App.Log($"Screensaver dismissed by pointer move (distance={distance:F1})");
            TriggerDismiss();
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if ((DateTime.UtcNow - _activationTime).TotalSeconds < 0.5) return;
        App.Log("Screensaver dismissed by pointer press");
        TriggerDismiss();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if ((DateTime.UtcNow - _activationTime).TotalSeconds < 0.5) return;
        App.Log("Screensaver dismissed by key press");
        TriggerDismiss();
    }

    private void TriggerDismiss()
    {
        if (_isClosing) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            DismissRequested?.Invoke();
        });
    }

    public void FadeOutAndClose(Action? onClosed = null)
    {
        if (_isClosing) return;
        _isClosing = true;

        DismissRequested = null;
        MediaBroadcastRequested = null;

        if (IsMaster)
        {
            AmbientAudioService.Stop();
        }

        _photoTimer.Stop();
        _videoWatchdogTimer.Stop();
        _clockTimer.Stop();
        _clockShuffleTimer.Stop();
        _oledShiftTimer.Stop();
        _blackoutStageTimer.Stop();
        _taskbarWatchdogTimer.Stop();

        _kenBurnsStoryboardA?.Stop();
        _kenBurnsStoryboardB?.Stop();
        _currentTransition?.Stop();

        var hWnd = WindowNative.GetWindowHandle(this);
        if (hWnd != IntPtr.Zero)
        {
            NativeMethods.MarkFullscreen(hWnd, false);
        }
        var startTime = DateTime.UtcNow;
        const double durationMs = 350.0;

        var curVid = _activeLayerIsA ? VideoA : VideoB;
        double startVolume = curVid.MediaPlayer?.Volume ?? 0;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (snd, _) =>
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            double progress = Math.Clamp(elapsed / durationMs, 0.0, 1.0);
            double eased = 1.0 - progress;

            byte alpha = (byte)(eased * 255);
            if (hWnd != IntPtr.Zero)
            {
                NativeMethods.SetLayeredWindowAttributes(hWnd, 0, alpha, NativeMethods.LWA_ALPHA);
            }
            if (RootWindowGrid != null)
            {
                RootWindowGrid.Opacity = eased;
            }

            // Audio ramp down
            if (curVid.MediaPlayer != null && startVolume > 0)
            {
                curVid.MediaPlayer.Volume = Math.Max(0, eased * startVolume);
            }

            if (progress >= 1.0)
            {
                timer.Stop();
                UnloadLayer(ImageA, VideoA, isLayerA: true);
                UnloadLayer(ImageB, VideoB, isLayerA: false);

                try
                {
                    if (_borderlessSubclassProc != null && hWnd != IntPtr.Zero)
                    {
                        NativeMethods.RemoveWindowSubclass(hWnd, _borderlessSubclassProc, (UIntPtr)101);
                        _borderlessSubclassProc = null;
                    }
                    Close();
                }
                catch (Exception ex)
                {
                    App.Log($"Error during Close in FadeOutAndClose: {ex.Message}");
                }

                onClosed?.Invoke();
            }
        };
        timer.Start();
    }

    private bool CanDisplayWidgets()
    {
        var s = _settingsService.Current;

        // When a specific display is targeted (not All Displays), show widgets on that targeted active monitor
        if (s.TargetDisplayIndex >= 0)
        {
            return DisplayIndex == s.TargetDisplayIndex;
        }

        int target = s.WidgetTargetDisplayIndex;
        if (target == -2) return true; // All Monitors
        if (target == -1) return _isPrimary; // Primary Only
        if (target >= 0)
        {
            var displays = DisplayArea.FindAll();
            if (target < displays.Count)
            {
                return DisplayIndex == target;
            }
            // Target monitor is disconnected/unavailable; gracefully fall back to primary
            return _isPrimary;
        }
        return _isPrimary;
    }

    private void UpdateClockWidget() => UpdateAllWidgets();

    private void UpdateAllWidgets()
    {
        ApplyVisualFilters();
        var s = _settingsService.Current;
        bool canShowWidgets = CanDisplayWidgets();
        bool enabled = s.WidgetsEnabled && canShowWidgets && !_isBlackout;
        var profile = s.GetWidgetProfileForDisplay(DisplayIndex);

        if (OverlayWidgetsLayer != null)
        {
            OverlayWidgetsLayer.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        // Clock Widget
        if (!enabled || !profile.ClockEnabled)
        {
            if (OverlayClockWidget != null) OverlayClockWidget.Visibility = Visibility.Collapsed;
            _clockShuffleTimer.Stop();
        }
        else
        {
            if (OverlayClockWidget != null) OverlayClockWidget.Visibility = Visibility.Visible;
            if (ClockWidgetControl != null)
            {
                ClockWidgetControl.DatePosition = s.DatePosition;
                ClockWidgetControl.DateFormat = s.DateFormat;
                ClockWidgetControl.Use24HourFormat = !s.ClockUse12Hour;
                ClockWidgetControl.ShowSeconds = s.ClockShowSeconds;
                ClockWidgetControl.ShowAmPm = s.ClockShowAmPm;
                ClockWidgetControl.ShowDate = s.ClockShowDate;
                ClockWidgetControl.ClockFontFamily = s.ClockFontFamily;
                ClockWidgetControl.ClockColorPreset = s.ClockColorPreset;
                ClockWidgetControl.ShowGlassPill = s.ShowGlassPill;
                ClockWidgetControl.GlassPillOpacity = s.GlassPillOpacity;
                ClockWidgetControl.ClockScale = s.ClockScale;
                ApplyClockPosition(profile.ClockPosition);
            }
        }

        // Weather Widget
        if (WeatherWidgetControl != null)
        {
            if (!enabled || !profile.WeatherEnabled)
            {
                WeatherWidgetControl.Visibility = Visibility.Collapsed;
            }
            else
            {
                WeatherWidgetControl.CityName = s.WeatherCity;
                WeatherWidgetControl.UseFahrenheit = s.WeatherUseFahrenheit;
                WeatherWidgetControl.WeatherScale = s.WeatherScale;
                WeatherWidgetControl.UseSystemAccent = s.WidgetUseSystemAccent;
                PlaceCardInZone(WeatherWidgetControl, profile.WeatherPosition);
                WeatherWidgetControl.Visibility = Visibility.Visible;
            }
        }

        // Calendar / Agenda Widget
        if (CalendarWidgetControl != null)
        {
            if (!enabled || !profile.CalendarEnabled)
            {
                CalendarWidgetControl.Visibility = Visibility.Collapsed;
            }
            else
            {
                CalendarWidgetControl.CalendarScale = s.CalendarScale;
                CalendarWidgetControl.UseSystemAccent = s.WidgetUseSystemAccent;
                CalendarWidgetControl.ShowWeekNumber = s.CalendarShowWeekNumber;
                CalendarWidgetControl.ShowEventNote = s.CalendarShowEventNote;
                CalendarWidgetControl.CustomNote = s.CalendarCustomNote;
                CalendarWidgetControl.DateFormatStyle = s.CalendarDateFormat;
                CalendarWidgetControl.UseLiveEvents = s.CalendarUseLiveEvents;
                CalendarWidgetControl.IcsUrlOrPath = s.CalendarIcsUrlOrPath;
                PlaceCardInZone(CalendarWidgetControl, profile.CalendarPosition);
                CalendarWidgetControl.Visibility = Visibility.Visible;
            }
        }

        // Media / Now Playing Widget
        if (MediaWidgetControl != null)
        {
            bool mediaEnabled = enabled && profile.MediaEnabled;
            MediaWidgetControl.IsWidgetEnabled = mediaEnabled;
            if (!mediaEnabled)
            {
                MediaWidgetControl.Visibility = Visibility.Collapsed;
            }
            else
            {
                MediaWidgetControl.MediaScale = s.MediaScale;
                MediaWidgetControl.UseSystemAccent = s.WidgetUseSystemAccent;
                MediaWidgetControl.ShowAlbumArt = s.MediaShowAlbumArt;
                MediaWidgetControl.HideWhenIdle = s.MediaHideWhenIdle;
                MediaWidgetControl.IdleText = s.MediaIdleText;
                MediaWidgetControl.AudioSpectrumEnabled = s.AudioSpectrumEnabled;
                PlaceCardInZone(MediaWidgetControl, profile.MediaPosition);
                MediaWidgetControl.ApplyVisibilityState();
            }
        }

        // Performance Monitor Widget
        if (PerformanceWidgetControl != null)
        {
            if (!enabled || !profile.PerformanceEnabled)
            {
                PerformanceWidgetControl.Visibility = Visibility.Collapsed;
            }
            else
            {
                PerformanceWidgetControl.PerformanceScale = s.PerformanceScale;
                PerformanceWidgetControl.UseSystemAccent = s.WidgetUseSystemAccent;
                PerformanceWidgetControl.ShowCpu = s.PerformanceShowCpu;
                PerformanceWidgetControl.ShowRam = s.PerformanceShowRam;
                PerformanceWidgetControl.ShowPercentages = s.PerformanceShowPercentages;
                PerformanceWidgetControl.HighlightThreshold = s.PerformanceHighlightThreshold;
                PerformanceWidgetControl.RefreshIntervalSeconds = s.PerformanceRefreshIntervalSeconds;
                PlaceCardInZone(PerformanceWidgetControl, profile.PerformancePosition);
                PerformanceWidgetControl.Visibility = Visibility.Visible;
            }
        }

        // Wisdom & Quotes Widget (Independent Literary Overlay — Separate from Utility Dock)
        if (!s.QuoteIndependentMigrated)
        {
            s.QuotePosition = WidgetPosition.Center;
            s.QuoteIndependentMigrated = true;
            _settingsService.Save();
        }

        if (!enabled || !profile.QuoteEnabled)
        {
            if (OverlayQuoteWidget != null) OverlayQuoteWidget.Visibility = Visibility.Collapsed;
            if (QuoteWidgetControl != null) QuoteWidgetControl.Visibility = Visibility.Collapsed;
        }
        else
        {
            if (OverlayQuoteWidget != null) OverlayQuoteWidget.Visibility = Visibility.Visible;
            if (QuoteWidgetControl != null)
            {
                QuoteWidgetControl.CardWidth = s.QuoteCardWidth;
                QuoteWidgetControl.QuoteScale = s.QuoteScale;
                QuoteWidgetControl.BackgroundOpacity = s.QuoteBackgroundOpacity;
                QuoteWidgetControl.ShowBorder = s.QuoteShowBorder;
                QuoteWidgetControl.UseBlurEffect = s.QuoteBlurEffect;
                QuoteWidgetControl.BlurIntensity = s.QuoteBlurIntensity;
                QuoteWidgetControl.BackdropTheme = s.QuoteBackdropTheme ?? "Acrylic";
                QuoteWidgetControl.QuoteAlignment = s.QuoteTextAlignment;
                QuoteWidgetControl.QuoteFontStyle = s.QuoteFontStyle ?? "Literary Serif";
                QuoteWidgetControl.MainFontSize = s.QuoteMainFontSize;
                QuoteWidgetControl.MeaningFontSize = s.QuoteMeaningFontSize;
                QuoteWidgetControl.ColorTheme = s.QuoteColorTheme ?? "Warm Cream & Muted Gold";
                QuoteWidgetControl.UseSystemAccent = s.WidgetUseSystemAccent;
                QuoteWidgetControl.ShowCategoryHeader = s.QuoteShowCategoryHeader;
                QuoteWidgetControl.ShowAuthor = s.QuoteShowAuthor;
                QuoteWidgetControl.ShowMeaning = s.QuoteShowMeaning;
                QuoteWidgetControl.DatabasePath = s.QuoteDatabasePath;
                QuoteWidgetControl.RotationIntervalSeconds = s.QuoteRotationIntervalSeconds;
                ApplyElementPosition(QuoteWidgetControl, profile.QuotePosition);
                QuoteWidgetControl.Visibility = Visibility.Visible;
            }
        }

        // Battery Status Indicator
        if (BorderBatteryBadge != null)
        {
            if (!s.WidgetsEnabled || !s.BatteryWidgetEnabled || !canShowWidgets || _isBlackout || !NativeMethods.GetSystemPowerStatus(out var pStatus) || pStatus.BatteryFlag == 128 || pStatus.BatteryLifePercent > 100)
            {
                BorderBatteryBadge.Visibility = Visibility.Collapsed;
            }
            else
            {
                BorderBatteryBadge.Visibility = Visibility.Visible;
                TxtBatteryPercent.Text = $"{pStatus.BatteryLifePercent}%";
                bool isCharging = pStatus.ACLineStatus == 1;
                IconBattery.Glyph = isCharging ? "\uEA93" : (pStatus.BatteryLifePercent > 60 ? "\uEBAA" : (pStatus.BatteryLifePercent > 20 ? "\uEBA6" : "\uEBA0"));
                TxtBatteryPercent.Foreground = (!isCharging && pStatus.BatteryLifePercent <= 20)
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100))
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 238, 238, 238));
            }
        }
    }

    private void PlaceCardInZone(FrameworkElement card, WidgetPosition pos)
    {
        var targetStack = pos switch
        {
            WidgetPosition.TopLeft => ZoneStackTopLeft,
            WidgetPosition.TopCenter => ZoneStackTopCenter,
            WidgetPosition.TopRight => ZoneStackTopRight,
            WidgetPosition.CenterLeft => ZoneStackCenterLeft,
            WidgetPosition.Center => ZoneStackCenter,
            WidgetPosition.CenterRight => ZoneStackCenterRight,
            WidgetPosition.BottomLeft => ZoneStackBottomLeft,
            WidgetPosition.BottomCenter => ZoneStackBottomCenter,
            WidgetPosition.BottomRight => ZoneStackBottomRight,
            _ => ZoneStackBottomCenter
        };

        if (targetStack == null) return;

        Microsoft.UI.Xaml.Controls.StackPanel?[] allStacks =
        [
            ZoneStackTopLeft, ZoneStackTopCenter, ZoneStackTopRight,
            ZoneStackCenterLeft, ZoneStackCenter, ZoneStackCenterRight,
            ZoneStackBottomLeft, ZoneStackBottomCenter, ZoneStackBottomRight
        ];

        foreach (var stack in allStacks)
        {
            if (stack != null && stack != targetStack && stack.Children.Contains(card))
            {
                stack.Children.Remove(card);
            }
        }

        if (card.Parent is Microsoft.UI.Xaml.Controls.Panel currentParent && currentParent != targetStack && currentParent.Children.Contains(card))
        {
            currentParent.Children.Remove(card);
        }

        if (!targetStack.Children.Contains(card))
        {
            targetStack.Children.Add(card);
        }
    }

    private void ApplyVisualFilters()
    {
        var s = _settingsService.Current;

        if (BrightnessDimLayer != null)
        {
            double dim = Math.Clamp(s.BrightnessDimPercent, 0, 80) / 100.0;
            BrightnessDimLayer.Opacity = dim;
            BrightnessDimLayer.Visibility = dim > 0.005 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (VisualFilterLayer != null)
        {
            switch (s.VisualFilter)
            {
                case VisualFilterPreset.WarmSepia:
                    VisualFilterLayer.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(52, 180, 120, 50));
                    VisualFilterLayer.Opacity = 1;
                    VisualFilterLayer.Visibility = Visibility.Visible;
                    break;

                case VisualFilterPreset.CinematicCool:
                    VisualFilterLayer.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(46, 20, 80, 160));
                    VisualFilterLayer.Opacity = 1;
                    VisualFilterLayer.Visibility = Visibility.Visible;
                    break;

                case VisualFilterPreset.HighContrastMono:
                    VisualFilterLayer.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(55, 30, 30, 30));
                    VisualFilterLayer.Opacity = 1;
                    VisualFilterLayer.Visibility = Visibility.Visible;
                    break;

                case VisualFilterPreset.VintageFilm:
                    VisualFilterLayer.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(42, 140, 120, 80));
                    VisualFilterLayer.Opacity = 1;
                    VisualFilterLayer.Visibility = Visibility.Visible;
                    break;

                case VisualFilterPreset.CyberpunkNeon:
                    var grad = new Microsoft.UI.Xaml.Media.LinearGradientBrush
                    {
                        StartPoint = new Windows.Foundation.Point(0, 0),
                        EndPoint = new Windows.Foundation.Point(1, 1)
                    };
                    grad.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = Windows.UI.Color.FromArgb(40, 255, 0, 128), Offset = 0.0 });
                    grad.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = Windows.UI.Color.FromArgb(40, 0, 200, 255), Offset = 1.0 });
                    VisualFilterLayer.Background = grad;
                    VisualFilterLayer.Opacity = 1;
                    VisualFilterLayer.Visibility = Visibility.Visible;
                    break;

                case VisualFilterPreset.None:
                default:
                    VisualFilterLayer.Visibility = Visibility.Collapsed;
                    break;
            }
        }
    }

    private async void UpdateMediaInfoPill(MediaItem item)
    {
        if (BorderMediaInfo == null || TxtMediaInfo == null) return;

        var s = _settingsService.Current;
        bool canShow = CanDisplayWidgets() && s.ShowMediaInfoPill && !_isBlackout;
        if (!canShow)
        {
            BorderMediaInfo.Visibility = Visibility.Collapsed;
            BorderMediaInfo.Opacity = 0;
            return;
        }

        BorderMediaInfo.Opacity = 0;
        BorderMediaInfo.Visibility = Visibility.Visible;

        string summary = "";
        if (item.IsVideo)
        {
            summary = $"{item.FileName} (Video)";
        }
        else
        {
            summary = await ExtractExifSummaryAsync(item.FilePath);
        }

        TxtMediaInfo.Text = summary;

        var anim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(500)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, BorderMediaInfo);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        sb.Begin();
    }

    private static async Task<string> ExtractExifSummaryAsync(string filePath)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            var props = await decoder.BitmapProperties.GetPropertiesAsync(new[]
            {
                "System.Photo.CameraModel",
                "System.Photo.FNumber",
                "System.Photo.ExposureTime",
                "System.Photo.ISOSpeed",
                "System.Photo.DateTaken"
            });

            var parts = new List<string>();
            if (props.TryGetValue("System.Photo.CameraModel", out var cameraVal) && cameraVal.Value is string camera && !string.IsNullOrWhiteSpace(camera))
            {
                parts.Add(camera.Trim());
            }

            string exposure = "";
            if (props.TryGetValue("System.Photo.FNumber", out var fVal) && fVal.Value is double fNum)
            {
                exposure += $"ƒ/{fNum:0.#} ";
            }
            if (props.TryGetValue("System.Photo.ExposureTime", out var expVal) && expVal.Value is double expSec)
            {
                if (expSec < 1.0 && expSec > 0)
                    exposure += $"1/{Math.Round(1.0 / expSec)}s ";
                else if (expSec >= 1.0)
                    exposure += $"{expSec:0.#}s ";
            }
            if (props.TryGetValue("System.Photo.ISOSpeed", out var isoVal) && isoVal.Value != null)
            {
                exposure += $"ISO {isoVal.Value}";
            }

            if (!string.IsNullOrWhiteSpace(exposure))
                parts.Add(exposure.Trim());

            if (props.TryGetValue("System.Photo.DateTaken", out var dateVal) && dateVal.Value is DateTimeOffset dto)
            {
                parts.Add(dto.LocalDateTime.ToString("MMM d, yyyy"));
            }

            if (parts.Count > 0)
            {
                return string.Join(" • ", parts);
            }

            return $"{System.IO.Path.GetFileName(filePath)} ({decoder.PixelWidth}×{decoder.PixelHeight})";
        }
        catch
        {
            return System.IO.Path.GetFileName(filePath);
        }
    }

    private void StartOledShiftTimer()
    {
        _oledShiftTimer.Stop();
        bool canShowWidgets = CanDisplayWidgets();
        if (!_settingsService.Current.OledPixelShiftEnabled || !canShowWidgets || _isBlackout)
            return;
        _oledShiftTimer.Start();
    }

    private void OnOledShiftTick(object? sender, object e)
    {
        if (OledShiftTransform == null || !_settingsService.Current.OledPixelShiftEnabled || _isBlackout || _isSecondStageBlackout)
            return;

        _oledShiftAngle += 0.95;
        if (_oledShiftAngle > Math.PI * 2) _oledShiftAngle -= Math.PI * 2;

        double targetX = Math.Round(Math.Cos(_oledShiftAngle) * 5.0);
        double targetY = Math.Round(Math.Sin(_oledShiftAngle * 2.0) * 5.0);

        var sb = new Storyboard();
        var animX = new DoubleAnimation
        {
            To = targetX,
            Duration = new Duration(TimeSpan.FromSeconds(1.2)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animX, OledShiftTransform);
        Storyboard.SetTargetProperty(animX, "X");
        sb.Children.Add(animX);

        var animY = new DoubleAnimation
        {
            To = targetY,
            Duration = new Duration(TimeSpan.FromSeconds(1.2)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animY, OledShiftTransform);
        Storyboard.SetTargetProperty(animY, "Y");
        sb.Children.Add(animY);

        sb.Begin();
    }

    private void StartBlackoutStageTimer()
    {
        _blackoutStageTimer.Stop();
        int minutes = _settingsService.Current.AutoBlackoutMinutes;
        if (minutes <= 0 || _isBlackout)
            return;

        _blackoutStageTimer.Interval = TimeSpan.FromMinutes(minutes);
        _blackoutStageTimer.Start();
    }

    private void OnBlackoutStageTick(object? sender, object e)
    {
        _blackoutStageTimer.Stop();
        if (_isClosing || _isBlackout || _isSecondStageBlackout)
            return;

        _isSecondStageBlackout = true;
        App.Log("Second-stage auto-blackout triggered: fading screen to black and releasing display sleep locks.");

        if (IsMaster)
        {
            AmbientAudioService.Stop();
        }

        _photoTimer.Stop();
        _videoWatchdogTimer.Stop();
        _clockTimer.Stop();
        _clockShuffleTimer.Stop();
        _oledShiftTimer.Stop();

        // Pause any playing video to conserve GPU power
        var curVid = _activeLayerIsA ? VideoA : VideoB;
        try
        {
            curVid.MediaPlayer?.Pause();
        }
        catch { }

        if (BlackoutScrim != null)
        {
            BlackoutScrim.Visibility = Visibility.Visible;
            var fadeAnim = new DoubleAnimation
            {
                From = BlackoutScrim.Opacity,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(900)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var sb = new Storyboard();
            Storyboard.SetTarget(fadeAnim, BlackoutScrim);
            Storyboard.SetTargetProperty(fadeAnim, "Opacity");
            sb.Children.Add(fadeAnim);
            sb.Begin();
        }

        // Release Windows display sleep restriction so monitors can enter standby
        NativeMethods.SetThreadExecutionState(NativeMethods.EXECUTION_STATE.ES_CONTINUOUS);
    }

    private void StartClockShuffleTimer()
    {
        _clockShuffleTimer.Stop();
        var s = _settingsService.Current;
        bool canShowWidgets = CanDisplayWidgets();
        if (!s.WidgetsEnabled || !s.ClockWidgetEnabled || !s.ClockShuffleEnabled || !canShowWidgets || _isBlackout)
            return;

        int seconds = Math.Max(5, s.ClockShuffleSeconds);
        _clockShuffleTimer.Interval = TimeSpan.FromSeconds(seconds);
        _clockShuffleTimer.Start();
    }

    private void OnClockShuffleTick(object? sender, object e)
    {
        if (ClockWidgetControl == null || OverlayClockWidget == null || OverlayClockWidget.Visibility != Visibility.Visible)
            return;

        var allPositions = (WidgetPosition[])Enum.GetValues(typeof(WidgetPosition));
        var candidates = allPositions.Where(p => p != _currentShufflePosition).ToArray();
        if (candidates.Length == 0) return;

        var nextPos = candidates[_rng.Next(candidates.Length)];
        _currentShufflePosition = nextPos;

        AnimateClockReposition(nextPos);
    }

    private void AnimateClockReposition(WidgetPosition nextPos)
    {
        if (ClockWidgetControl == null) return;

        var fadeOutAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = ClockWidgetControl.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(600),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut }
        };

        var fadeOutStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeOutAnim, ClockWidgetControl);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeOutAnim, "Opacity");
        fadeOutStoryboard.Children.Add(fadeOutAnim);

        fadeOutStoryboard.Completed += (_, _) =>
        {
            ApplyClockPosition(nextPos);

            var fadeInAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(600),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut }
            };

            var fadeInStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeInAnim, ClockWidgetControl);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeInAnim, "Opacity");
            fadeInStoryboard.Children.Add(fadeInAnim);
            fadeInStoryboard.Begin();
        };

        fadeOutStoryboard.Begin();
    }

    private void ApplyClockPosition(WidgetPosition pos)
    {
        ApplyElementPosition(ClockWidgetControl, pos);
    }

    private void ApplyElementPosition(FrameworkElement? element, WidgetPosition pos)
    {
        if (element == null) return;

        var (hAlign, vAlign, margin) = pos switch
        {
            WidgetPosition.TopLeft => (HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(56, 48, 0, 0)),
            WidgetPosition.TopCenter => (HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, 108, 0, 0)),
            WidgetPosition.TopRight => (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, 48, 56, 0)),
            WidgetPosition.CenterLeft => (HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(56, 0, 0, 0)),
            WidgetPosition.Center => (HorizontalAlignment.Center, VerticalAlignment.Center, new Thickness(0)),
            WidgetPosition.CenterRight => (HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0, 0, 56, 0)),
            WidgetPosition.BottomLeft => (HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(56, 0, 0, 48)),
            WidgetPosition.BottomCenter => (HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, 48)),
            WidgetPosition.BottomRight => (HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, 56, 48)),
            _ => (HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, 108, 0, 0))
        };

        element.HorizontalAlignment = hAlign;
        element.VerticalAlignment = vAlign;
        element.Margin = margin;
    }

    private static bool TryParseHexColor(string? hex, out Windows.UI.Color color)
    {
        color = Windows.UI.Color.FromArgb(255, 0, 82, 204);
        if (string.IsNullOrEmpty(hex)) return false;
        hex = hex.TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
        {
            color = Windows.UI.Color.FromArgb(255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            return true;
        }
        if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint argb))
        {
            color = Windows.UI.Color.FromArgb((byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
            return true;
        }
        return false;
    }
}
