using System;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LivelyScreen.Native;

namespace LivelyScreen_WinUI.Controls;

public sealed partial class StylizedPerformanceWidget : UserControl
{
    private readonly DispatcherTimer _perfTimer = new();
    private readonly DispatcherTimer _animTimer = new();

    private long _prevIdleTime = 0;
    private long _prevKernelTime = 0;
    private long _prevUserTime = 0;

    private double _currentCpu = 0;
    private double _targetCpu = 0;
    private double _smoothedRawCpu = 0;
    private bool _hasFirstCpuSample = false;

    private double _currentRam = 0;
    private double _targetRam = 0;
    private bool _hasFirstRamSample = false;

    public static readonly DependencyProperty PerformanceScaleProperty =
        DependencyProperty.Register(nameof(PerformanceScale), typeof(double), typeof(StylizedPerformanceWidget), new PropertyMetadata(1.0, OnScaleChanged));

    public static readonly DependencyProperty UseSystemAccentProperty =
        DependencyProperty.Register(nameof(UseSystemAccent), typeof(bool), typeof(StylizedPerformanceWidget), new PropertyMetadata(false, OnAccentChanged));

    public static readonly DependencyProperty ShowCpuProperty =
        DependencyProperty.Register(nameof(ShowCpu), typeof(bool), typeof(StylizedPerformanceWidget), new PropertyMetadata(true, OnDisplayConfigChanged));

    public static readonly DependencyProperty ShowRamProperty =
        DependencyProperty.Register(nameof(ShowRam), typeof(bool), typeof(StylizedPerformanceWidget), new PropertyMetadata(true, OnDisplayConfigChanged));

    public static readonly DependencyProperty ShowPercentagesProperty =
        DependencyProperty.Register(nameof(ShowPercentages), typeof(bool), typeof(StylizedPerformanceWidget), new PropertyMetadata(true, OnDisplayConfigChanged));

    public static readonly DependencyProperty HighlightThresholdProperty =
        DependencyProperty.Register(nameof(HighlightThreshold), typeof(int), typeof(StylizedPerformanceWidget), new PropertyMetadata(85, OnDisplayConfigChanged));

    public static readonly DependencyProperty RefreshIntervalSecondsProperty =
        DependencyProperty.Register(nameof(RefreshIntervalSeconds), typeof(int), typeof(StylizedPerformanceWidget), new PropertyMetadata(2, OnIntervalChanged));

    public double PerformanceScale
    {
        get => (double)GetValue(PerformanceScaleProperty);
        set => SetValue(PerformanceScaleProperty, value);
    }

    public bool UseSystemAccent
    {
        get => (bool)GetValue(UseSystemAccentProperty);
        set => SetValue(UseSystemAccentProperty, value);
    }

    public bool ShowCpu
    {
        get => (bool)GetValue(ShowCpuProperty);
        set => SetValue(ShowCpuProperty, value);
    }

    public bool ShowRam
    {
        get => (bool)GetValue(ShowRamProperty);
        set => SetValue(ShowRamProperty, value);
    }

    public bool ShowPercentages
    {
        get => (bool)GetValue(ShowPercentagesProperty);
        set => SetValue(ShowPercentagesProperty, value);
    }

    public int HighlightThreshold
    {
        get => (int)GetValue(HighlightThresholdProperty);
        set => SetValue(HighlightThresholdProperty, value);
    }

    public int RefreshIntervalSeconds
    {
        get => (int)GetValue(RefreshIntervalSecondsProperty);
        set => SetValue(RefreshIntervalSecondsProperty, value);
    }

    public StylizedPerformanceWidget()
    {
        this.InitializeComponent();
        ApplyScale(PerformanceScale);

        _perfTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, RefreshIntervalSeconds));
        _perfTimer.Tick += (st, et) =>
        {
            if (Visibility == Visibility.Visible)
                UpdateStats();
        };

        _animTimer.Interval = TimeSpan.FromMilliseconds(16);
        _animTimer.Tick += (st, et) => StepAnimation();

        this.Loaded += (s, e) =>
        {
            SampleCpuTimes();
            UpdateStats();
            _perfTimer.Start();
        };

        this.Unloaded += (s, e) =>
        {
            _perfTimer.Stop();
            _animTimer.Stop();
        };
    }

    private static void OnIntervalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedPerformanceWidget w && e.NewValue is int sec)
        {
            w._perfTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, sec));
        }
    }

    private static void OnDisplayConfigChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedPerformanceWidget w)
        {
            w.UpdateStats();
        }
    }

    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedPerformanceWidget w && e.NewValue is double s)
        {
            w.ApplyScale(s);
        }
    }

    private static void OnAccentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedPerformanceWidget w)
        {
            w.ApplyAccentPalette();
        }
    }

    private void ApplyScale(double scale)
    {
        if (RootContainer != null)
        {
            double s = Math.Max(0.1, scale);
            RootContainer.Width = Math.Round(244 * s);
            RootContainer.Height = Math.Round(108 * s);
        }
    }

    private Brush GetNormalBrush()
    {
        if (UseSystemAccent && Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accentObj) && accentObj is Brush accentBrush)
        {
            return accentBrush;
        }
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 205, 255));
    }

    private static readonly Brush _warningBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 140, 60));

    private void ApplyAccentPalette()
    {
        RenderMeters();
    }

    private void StepAnimation()
    {
        bool cpuMoving = Math.Abs(_currentCpu - _targetCpu) > 0.05;
        bool ramMoving = Math.Abs(_currentRam - _targetRam) > 0.05;

        if (!cpuMoving && !ramMoving)
        {
            _currentCpu = _targetCpu;
            _currentRam = _targetRam;
            RenderMeters();
            _animTimer.Stop();
            return;
        }

        const double lerpFactor = 0.18; // smooth ease-out curve (~350ms settle time)

        if (cpuMoving)
        {
            _currentCpu += (_targetCpu - _currentCpu) * lerpFactor;
            if (Math.Abs(_currentCpu - _targetCpu) < 0.1)
                _currentCpu = _targetCpu;
        }

        if (ramMoving)
        {
            _currentRam += (_targetRam - _currentRam) * lerpFactor;
            if (Math.Abs(_currentRam - _targetRam) < 0.1)
                _currentRam = _targetRam;
        }

        RenderMeters();
    }

    private void RenderMeters()
    {
        var normalBrush = GetNormalBrush();

        if (ProgressCpu != null && TxtCpuVal != null && ShowCpu)
        {
            ProgressCpu.Value = _currentCpu;
            TxtCpuVal.Text = $"{Math.Round(_currentCpu)}%";
            ProgressCpu.Foreground = (HighlightThreshold > 0 && _currentCpu >= HighlightThreshold) ? _warningBrush : normalBrush;
        }

        if (ProgressRam != null && TxtRamVal != null && ShowRam)
        {
            ProgressRam.Value = _currentRam;
            TxtRamVal.Text = $"{Math.Round(_currentRam)}%";
            ProgressRam.Foreground = (HighlightThreshold > 0 && _currentRam >= HighlightThreshold) ? _warningBrush : normalBrush;
        }
    }

    private static long ToLong(FILETIME ft)
    {
        return ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
    }

    private void SampleCpuTimes()
    {
        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _prevIdleTime = ToLong(idle);
            _prevKernelTime = ToLong(kernel);
            _prevUserTime = ToLong(user);
        }
    }

    private void UpdateStats()
    {
        try
        {
            if (PanelCpuMeter != null) PanelCpuMeter.Visibility = ShowCpu ? Visibility.Visible : Visibility.Collapsed;
            if (PanelRamMeter != null) PanelRamMeter.Visibility = ShowRam ? Visibility.Visible : Visibility.Collapsed;
            if (TxtCpuVal != null) TxtCpuVal.Visibility = ShowPercentages ? Visibility.Visible : Visibility.Collapsed;
            if (TxtRamVal != null) TxtRamVal.Visibility = ShowPercentages ? Visibility.Visible : Visibility.Collapsed;

            if (ShowCpu && NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
            {
                long curIdle = ToLong(idle);
                long curKernel = ToLong(kernel);
                long curUser = ToLong(user);

                long deltaIdle = curIdle - _prevIdleTime;
                long deltaTotal = (curKernel - _prevKernelTime) + (curUser - _prevUserTime);

                _prevIdleTime = curIdle;
                _prevKernelTime = curKernel;
                _prevUserTime = curUser;

                if (deltaTotal > 0)
                {
                    double rawCpu = (1.0 - ((double)deltaIdle / deltaTotal)) * 100.0;
                    rawCpu = Math.Clamp(rawCpu, 0, 100);

                    if (!_hasFirstCpuSample)
                    {
                        _hasFirstCpuSample = true;
                        _smoothedRawCpu = rawCpu;
                        _targetCpu = rawCpu;
                        _currentCpu = rawCpu;
                    }
                    else
                    {
                        // Light EMA (70% current reading, 30% history) to soften single-frame spikes
                        _smoothedRawCpu = (_smoothedRawCpu * 0.3) + (rawCpu * 0.7);
                        _targetCpu = _smoothedRawCpu;
                    }
                }
            }

            if (ShowRam)
            {
                var mem = new NativeMethods.MEMORYSTATUSEX();
                if (NativeMethods.GlobalMemoryStatusEx(mem))
                {
                    double ramUsage = mem.dwMemoryLoad;
                    if (!_hasFirstRamSample)
                    {
                        _hasFirstRamSample = true;
                        _targetRam = ramUsage;
                        _currentRam = ramUsage;
                    }
                    else
                    {
                        _targetRam = ramUsage;
                    }
                }
            }

            RenderMeters();

            if (!_animTimer.IsEnabled && (Math.Abs(_currentCpu - _targetCpu) > 0.05 || Math.Abs(_currentRam - _targetRam) > 0.05))
            {
                _animTimer.Start();
            }
        }
        catch { }
    }
}
