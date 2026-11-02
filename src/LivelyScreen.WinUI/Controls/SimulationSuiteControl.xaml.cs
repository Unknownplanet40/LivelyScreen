using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using Windows.UI;
using LivelyScreen.Models;
using LivelyScreen.Native;
using LivelyScreen_WinUI.ViewModels;

namespace LivelyScreen_WinUI.Controls;

public sealed partial class SimulationSuiteControl : UserControl
{
    // =========================================================================
    // MULTI-MONITOR MODE SETTING ("primary_only" default, or "mirror")
    // =========================================================================
    public const string MULTI_MONITOR_PRIMARY_ONLY = AppSettings.MultiMonitorModePrimaryOnly; // "primary_only"
    public const string MULTI_MONITOR_MIRROR = AppSettings.MultiMonitorModeMirror;             // "mirror"
    public static string MULTI_MONITOR_MODE = MULTI_MONITOR_PRIMARY_ONLY;

    // =========================================================================
    // WINDOWS 10 / 11 SPINNER & LAYOUT CONSTANTS (MEASURED FROM 30 FPS REFERENCE)
    // =========================================================================
    public const double LOOP_S = 4.0;                       // 4.0s loop (2 full laps per loop)
    public const double STAGGER_S = 0.125;                  // 125ms stagger per dot (tau = t - 0.125*j)
    public const int DOT_COUNT = 6;                         // 6 solid white round dots
    public const double SAMPLE_STEP_S = 0.1;                // Sampled every 0.1s from t = 0 to 4.0s
    public const double DOT_EXTRA_OFFSET_DEG = 8.7;         // Extra angular offset per dot: -8.7*j
    public const double SPEED = 1.0;                        // Single speed multiplier on elapsed time

    // Lead dot (dot 0) unwrapped angle curve f(t) in degrees, sampled every 0.1s (0.0s .. 4.0s)
    public static readonly double[] LEAD_KEYFRAMES =
    [
        13, 22, 33, 55, 95, 124, 172, 216, 249, 271,
        283, 292, 301, 310, 321, 327, 336, 344, 354, 364,
        373, 382, 391, 400, 411, 417, 427, 446, 478, 520,
        576, 618, 650, 669, 681, 687, 696, 705, 714, 723,
        731
    ];

    // Layout & size fractions of screen height (measured from 1365x767 reference)
    public const double RING_DIAMETER_HEIGHT_FRAC = 0.0645; // ~49.5px at 767px height (6.45% of screen height)
    public const double DOT_DIAMETER_HEIGHT_FRAC = 0.0068;  // ~5.2px at 767px height (0.68% of screen height, ring / 9.6)
    public const double RING_CENTER_X_FRAC = 0.50;          // Horizontally centered (50%)
    public const double RING_CENTER_Y_FRAC = 0.359;         // Ring center y = 35.9% of screen height (~275px at 767p)
    public const double LINE1_CENTER_Y_FRAC = 0.434;        // Line 1 center y = 43.4% of screen height
    public const double LINE2_CENTER_Y_FRAC = 0.480;        // Line 2 center y = 48.0% of screen height
    public const double BOTTOM_LINE_CENTER_Y_FRAC = 0.880;  // Bottom line center y = 88.0% of screen height
    public const double FONT_SIZE_HEIGHT_FRAC = 0.027;      // Font size = 2.7% of screen height (~20.7px at 767p)

    // =========================================================================
    // WINDOWS 10 BLUE BSOD & WINDOWS 11 24H2+ BLACK BSOD CONSTANTS
    // =========================================================================
    public static readonly Color WIN10_BSOD_BG_COLOR = Color.FromArgb(255, 0, 120, 215);    // #0078D7
    public static readonly Color WIN11_BSOD_BG_COLOR = Color.FromArgb(255, 0, 0, 0);        // #000000
    public const double WIN10_BSOD_LEFT_X_FRAC = 0.1094;          // ~11% of screen width (x=112 at 1024w)
    public const double WIN10_BSOD_SAD_TOP_Y_FRAC = 0.170;        // ":(" top at ~17% of height
    public const double WIN10_BSOD_SAD_FONT_H_FRAC = 0.145;       // ":(" font size (~14.5% of height)
    public const double WIN10_BSOD_MAIN_FONT_H_FRAC = 0.033;      // Main text & N% font size (~3.3% of height)
    public const double WIN10_BSOD_LINE1_TOP_Y_FRAC = 0.3496;     // Line 1 top at ~35.0% of height
    public const double WIN10_BSOD_LINE2_TOP_Y_FRAC = 0.3930;     // Line 2 top at ~39.3% of height
    public const double WIN10_BSOD_PCT_TOP_Y_FRAC = 0.470;        // "N% complete" at ~47% of height
    public const double WIN10_BSOD_QR_TOP_Y_FRAC = 0.6017;        // QR top (~60.2% H, center ~66% H)
    public const double WIN10_BSOD_QR_SIZE_H_FRAC = 0.116;        // QR square size (11.6% of height)
    public const double WIN10_BSOD_SMALL_FONT_H_FRAC = 0.014;     // Small text next to QR (~1.4% of height)
    public const double WIN10_BSOD_INFO_GAP_W_FRAC = 0.012;       // Horizontal gap between QR and info text
    public const double WIN10_BSOD_URL_TOP_Y_FRAC = 0.6017;       // Line 1 right of QR ("For more information...")
    public const double WIN10_BSOD_SUPPORT_TOP_Y_FRAC = 0.6435;   // Line 2 right of QR ("If you call a support person...")
    public const double WIN10_BSOD_STOP_TOP_Y_FRAC = 0.6696;      // Line 3 right of QR ("Stop code: ...")

    // [UNVERIFIED LAYOUT AGAINST REAL CAPTURE]: Windows 11 24H2+ Black BSOD layout fractions (measured from win11_black_screen_mockup.png)
    public const double WIN11_BSOD_MAIN_CENTER_Y_FRAC = 0.4504;   // Centered main message y = 45.0% H
    public const double WIN11_BSOD_PCT_CENTER_Y_FRAC = 0.5096;    // Centered "N% complete" y = 51.0% H
    public const double WIN11_BSOD_MAIN_FONT_H_FRAC = 0.033;      // Main text & N% font size (3.3% H)
    public const double WIN11_BSOD_BOTTOM_LEFT_X_FRAC = 0.1094;   // Bottom-left margin (~11% W)
    public const double WIN11_BSOD_STOP_TOP_Y_FRAC = 0.8730;      // "Stop code: <CODE>" at 87.3% H
    public const double WIN11_BSOD_FAILED_TOP_Y_FRAC = 0.9026;    // "What failed: <driver>.sys" at 90.3% H
    public const double WIN11_BSOD_SMALL_FONT_H_FRAC = 0.0165;    // Bottom stop code / driver font size

    // =========================================================================
    // WINDOWS RESTART & BOOT SEQUENCE CONSTANTS
    // =========================================================================
    public const double BSOD_DUMP_DURATION_S = 6.5;               // Irregular crash dump duration 0% -> 100%
    public const double BSOD_HOLD_100_S = 0.6;                    // Brief hold at 100% before screen goes black
    public const double RESTARTING_SCREEN_S = 2.0;                // "Restarting" screen duration after update reaches 100%
    public const double BLACK_SCREEN_S = 1.5;                     // 1.5s black screen before boot screen
    public const double BOOT_S = 6.0;                             // 6.0s Windows boot screen duration per loop
    public const double BOOT_LOGO_FADE_IN_S = 0.4;                // 0.4s logo fade-in duration
    public const double BOOT_LOGO_CENTER_Y_FRAC = 0.40;           // Windows boot logo center at 40% of height
    public const double BOOT_LOGO_SIZE_H_FRAC = 0.12;             // Windows boot logo height (~12% of height)
    public const double BOOT_SPINNER_CENTER_Y_FRAC = 0.84;        // Boot dot spinner near bottom of screen (84% of height)

    // Irregular crash dump keyframes (normalized time 0..1 -> percentage 0..100 with pauses)
    public static readonly (double TimeFrac, int Percent)[] BSOD_IRREGULAR_STEPS =
    [
        (0.00, 0),
        (0.14, 0),    // initial pause at 0%
        (0.20, 15),   // jump to 15%
        (0.36, 20),   // pause around 20%
        (0.45, 45),   // jump to 45%
        (0.64, 50),   // stall at 50%
        (0.74, 80),   // burst to 80%
        (0.90, 85),   // brief pause at 85%
        (0.96, 100),  // finish at 100%
        (1.00, 100)
    ];

    // =========================================================================
    // MACOS BOOT/UPDATE & KERNEL PANIC CONSTANTS (MEASURED FROM 1365x767 REF)
    // =========================================================================
    public const double MAC_LOGO_WIDTH_FRAC = 0.081319;     // 111px / 1365px (8.1% of width)
    public const double MAC_LOGO_HEIGHT_FRAC = 0.178618;    // 137px / 767px (17.9% of height)
    public const double MAC_LOGO_CENTER_Y_FRAC = 0.445893;  // Center y = 44.6% of height (bbox y 274-410 at 767p)
    public const double MAC_BAR_WIDTH_FRAC = 0.234432;      // 320px / 1365px (23.4% of width, x 523-842)
    public const double MAC_BAR_HEIGHT_FRAC = 0.007823;     // 6px / 767px (0.78% of height, y 488-494)
    public const double MAC_BAR_CENTER_Y_FRAC = 0.640156;   // Center y = 64.0% of height (y = 491 at 767p)
    public static readonly Color MAC_UPDATE_BG_COLOR = Color.FromArgb(255, 0, 0, 0);          // #000000
    public static readonly Color MAC_BAR_TRACK_COLOR = Color.FromArgb(255, 192, 192, 192);    // #C0C0C0
    public static readonly Color MAC_BAR_FILL_COLOR = Color.FromArgb(255, 255, 255, 255);     // #FFFFFF
    public static readonly Color MAC_PANIC_BG_COLOR = Color.FromArgb(255, 26, 26, 26);        // #1A1A1A
    public static readonly Color MAC_PANIC_TEXT_COLOR = Color.FromArgb(255, 250, 250, 250);   // #FAFAFA
    public const double MAC_PANIC_FONT_HEIGHT_FRAC = 0.0235; // ~18px at 767p
    public static readonly double[] MAC_PANIC_LINE_CENTER_Y_FRACS =
    [
        0.2096, 0.2443, // English (2 lines)
        0.3217, 0.3548, // French (2 lines)
        0.4339, 0.4687, // German (2 lines)
        0.5496, 0.5835, // Japanese (2 lines)
        0.6574, 0.6922  // Spanish (2 lines)
    ];

    // =========================================================================
    // UBUNTU BOOT/UPDATE & KERNEL PANIC CONSTANTS (MEASURED FROM 1365x767 REF)
    // =========================================================================
    public static readonly Color UBUNTU_UPDATE_BG_COLOR = Color.FromArgb(255, 44, 2, 32);     // #2C0220 (aubergine)
    public static readonly Color UBUNTU_DOT_ORANGE_COLOR = Color.FromArgb(255, 252, 61, 4);   // #FC3D04
    public const double UBUNTU_WORDMARK_X_FRAC = 539.0 / 1365.0;  // x = 539 at 1365w
    public const double UBUNTU_WORDMARK_Y_FRAC = 335.0 / 767.0;   // y = 335 at 767h
    public const double UBUNTU_WORDMARK_W_FRAC = 314.0 / 1365.0;  // 314px (23.0% of width)
    public const double UBUNTU_WORDMARK_H_FRAC = 66.0 / 767.0;    // 66px (8.6% of height)
    public const double UBUNTU_DOT_DIAMETER_H_FRAC = 0.01043;     // ~8px at 767h (1.04% of height)
    public const double UBUNTU_DOT_CENTER_Y_FRAC = 443.0 / 767.0; // y = 443 at 767h (57.8% of height)
    public static readonly double[] UBUNTU_DOT_CENTER_X_FRACS =
    [
        595.0 / 1365.0,
        639.0 / 1365.0,
        683.0 / 1365.0,
        727.0 / 1365.0,
        771.0 / 1365.0
    ];
    // [UNVERIFIED PLACEHOLDER TIMING]: 1.2s cycle with 150ms stagger per dot
    public const double UBUNTU_DOT_CYCLE_S = 1.2;
    public const double UBUNTU_DOT_STAGGER_S = 0.15;

    public static readonly Color UBUNTU_PANIC_BG_COLOR = Color.FromArgb(255, 0, 0, 0);        // #000000
    public static readonly Color UBUNTU_PANIC_TEXT_COLOR = Color.FromArgb(255, 210, 210, 210);// #D2D2D2 light gray
    public const double UBUNTU_PANIC_LEFT_X_FRAC = 0.00879;       // x = 12px at 1365w (9px at 1024w)
    public const double UBUNTU_PANIC_FIRST_CENTER_Y_FRAC = 0.0304;// y = 23.3px at 767h
    public const double UBUNTU_PANIC_LINE_STEP_Y_FRAC = 0.0287;   // 22.0px line step at 767h
    public const double UBUNTU_PANIC_FONT_H_FRAC = 0.0209;        // ~16px at 767h
    public static readonly string[] UBUNTU_PANIC_LINES =
    [
        "[    2.418237] Kernel panic - not syncing: VFS: Unable to mount root fs on unknown-block(0,0)",
        "[    2.418911] CPU: 1 PID: 1 Comm: swapper/0 Not tainted 5.15.0-91-generic #101-Ubuntu",
        "[    2.419402] Hardware name: Dell Inc. Inspiron 15 3511/0XYZ12, BIOS 1.12.0 03/14/2023",
        "[    2.419880] Call Trace:",
        "[    2.420104]  <TASK>",
        "[    2.420233]  show_stack+0x52/0x5c",
        "[    2.420431]  dump_stack_lvl+0x4a/0x63",
        "[    2.420655]  dump_stack+0x10/0x16",
        "[    2.420872]  panic+0x149/0x321",
        "[    2.421034]  mount_block_root+0x21e/0x2d4",
        "[    2.421290]  mount_root+0x38/0x3e",
        "[    2.421511]  prepare_namespace+0x13f/0x19e",
        "[    2.421768]  kernel_init_freeable+0x25b/0x28b",
        "[    2.422019]  ? rest_init+0x100/0x100",
        "[    2.422234]  kernel_init+0x1b/0x150",
        "[    2.422450]  ret_from_fork+0x22/0x30",
        "[    2.422671]  </TASK>",
        "[    2.423102] Kernel Offset: 0x2a000000 from 0xffffffff81000000 (relocation range: 0xffffffff80000000-0xffffffffbfffffff)",
        "[    2.423655] ---[ end Kernel panic - not syncing: VFS: Unable to mount root fs on unknown-block(0,0) ]---"
    ];

    // =========================================================================
    // CHROMEOS BOOT/UPDATE & RECOVERY CRASH CONSTANTS (MEASURED FROM 1365x767)
    // =========================================================================
    public static readonly Color CHROME_BG_COLOR = Color.FromArgb(255, 255, 255, 255);        // #FFFFFF
    public static readonly Color CHROME_SPINNER_COLOR = Color.FromArgb(255, 54, 125, 243);    // #367DF3
    public static readonly Color CHROME_TITLE_COLOR = Color.FromArgb(255, 32, 33, 36);        // #202124
    public static readonly Color CHROME_SUBTITLE_COLOR = Color.FromArgb(255, 95, 99, 104);    // #5F6368
    public static readonly Color CHROME_BAR_TRACK_COLOR = Color.FromArgb(255, 219, 221, 225); // #DBDDE1
    public static readonly Color CHROME_BAR_FILL_COLOR = Color.FromArgb(255, 69, 134, 244);   // #4586F4
    public static readonly Color CHROME_CRASH_ICON_COLOR = Color.FromArgb(255, 217, 48, 37);  // #D93025
    public const double CHROME_SPINNER_DIAM_H_FRAC = 0.0680;       // ~52.2px outer box at 767h
    public const double CHROME_SPINNER_STROKE_H_FRAC = 0.0060;     // ~4.6px stroke at 767h
    public const double CHROME_SPINNER_CENTER_Y_FRAC = 0.3913;     // y = 300.1px at 767h
    public const double CHROME_UPD_TITLE_CENTER_Y_FRAC = 0.4913;   // y = 376.8px at 767h
    public const double CHROME_UPD_TITLE_FONT_H_FRAC = 0.0365;     // ~28px at 767h
    public const double CHROME_UPD_SUB_CENTER_Y_FRAC = 0.5417;     // y = 415.5px at 767h
    public const double CHROME_UPD_SUB_FONT_H_FRAC = 0.0218;       // ~16.7px at 767h
    public const double CHROME_BAR_WIDTH_W_FRAC = 0.293945;        // 401.2px at 1365w
    public const double CHROME_BAR_HEIGHT_H_FRAC = 0.007823;       // 6.0px at 767h
    public const double CHROME_BAR_CENTER_Y_FRAC = 0.615652;       // y = 472.2px at 767h
    // [UNVERIFIED PLACEHOLDER TIMING]: Standard Material 1.4s expand/collapse arc cycle
    public const double CHROME_SPINNER_CYCLE_S = 1.4;

    public const double CHROME_CRASH_ICON_DIAM_H_FRAC = 0.09217;   // 70.7px at 767h (53px at 575h)
    public const double CHROME_CRASH_ICON_CENTER_Y_FRAC = 0.3513;  // y = 269.5px at 767h
    public const double CHROME_CRASH_TITLE_CENTER_Y_FRAC = 0.4661; // y = 357.5px at 767h
    public const double CHROME_CRASH_TITLE_FONT_H_FRAC = 0.0378;   // ~29px at 767h
    public const double CHROME_CRASH_SUB_CENTER_Y_FRAC = 0.5252;   // y = 402.8px at 767h
    public const double CHROME_CRASH_SUB_FONT_H_FRAC = 0.0244;     // ~18.7px at 767h

    // =========================================================================
    // SHARED MULTI-MONITOR SYNCHRONIZATION CLOCK & SESSION STATE
    // Single clock, single fixed random seed, and single timer across all windows
    // =========================================================================
    private static readonly List<SimulationSuiteControl> SharedActiveControls = new();
    private static DispatcherTimer? _sharedTelemetryTimer;   // Shared 250ms state & telemetry timer
    private static bool _sharedRenderingHooked = false;
    private static bool _sharedSessionActive = false;
    private static long _sharedSessionStartTicks = 0;
    private static long _sharedCycleStartTicks = 0;
    private static int _sharedRandomSeed = 0x5A17C3D9;
    private static SimulationModeType _sharedEffectiveMode = SimulationModeType.Windows11Update;
    private static bool _sharedIsBsodActive = false;
    private static long? _sharedBsodStartTicks = null;
    private static bool _sharedWasEnterDown = true;
    private static bool _sharedWasEscapeDown = true;
    private static bool _sharedWasSpaceDown = true;
    private static DateTime _sharedLastKeyHandledUtc = DateTime.MinValue;

    private SimulationModeType _activeMode = SimulationModeType.None;
    private AppSettings? _settings;
    private DateTime _sessionStartUtc => new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Stopwatch.GetElapsedTime(0, _sharedSessionStartTicks).TotalSeconds);
    private DateTime _cycleStartUtc
    {
        get => DateTime.UtcNow.AddSeconds(-GetSharedCycleElapsedSeconds());
        set => _sharedCycleStartTicks = Stopwatch.GetTimestamp() - (long)((DateTime.UtcNow - value).TotalSeconds * Stopwatch.Frequency);
    }
    private int _currentPercent = 7;
    private int _bsodPercent = 0;
    private bool _isBsodActive
    {
        get => _sharedIsBsodActive;
        set => _sharedIsBsodActive = value;
    }
    private int _remainingDelayTicks = 0;
    private double _animTimeSec = 0;
    private int _telemetryTickCounter = 0;

    public FakeUpdateSimulationViewModel ViewModel { get; } = new();
    private DateTime? _bsodStartedAtUtc = null;
    private DateTime? _bootPhaseStartUtc = null;
    private bool _isWindowsBootPhase = false;

    private readonly List<Ellipse> _winSpinnerEllipses = new();
    private readonly List<Visual> _winSpinnerDotVisuals = new();
    private readonly List<TextBlock> _ubuntuPanicTextBlocks = new();
    private readonly HackerDashboardCanvasControl _hackerDashboardControl;
    private static readonly double[] _pchipSlopes = ComputePeriodicPchipSlopes();

    public event Action? RequestDismiss;

    private static readonly SolidColorBrush BrushUbuntuOrange = new(Color.FromArgb(255, 252, 61, 4)); // #fc3d04 from whitescreen.online
    private static readonly SolidColorBrush BrushWhite = new(Color.FromArgb(255, 255, 255, 255));

    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

    public SimulationSuiteControl()
    {
        InitializeComponent();
        _hackerDashboardControl = new HackerDashboardCanvasControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        PanelCyberOps.Children.Add(_hackerDashboardControl);
        BuildWindowsSpinnerDots();
        BuildUbuntuPanicConsoleLines();
        BuildAuthenticBsodQrCode();
        LoadBrandLogoAssets();
        SizeChanged += OnSimulationControlSizeChanged;

        Unloaded += (_, _) => StopSimulation();
    }

    private static double GetSharedSessionElapsedSeconds()
    {
        if (_sharedSessionStartTicks == 0) return 0.0;
        return Math.Max(0.0, Stopwatch.GetElapsedTime(_sharedSessionStartTicks).TotalSeconds);
    }

    private static double GetSharedCycleElapsedSeconds()
    {
        if (_sharedCycleStartTicks == 0) return 0.0;
        return Math.Max(0.0, Stopwatch.GetElapsedTime(_sharedCycleStartTicks).TotalSeconds);
    }

    /// <summary>
    /// Initializes a single shared simulation clock and fixed random seed across all monitors.
    /// All mirrored windows evaluate animations and percentage progression from this shared clock.
    /// </summary>
    public static void BeginSharedSession(SimulationModeType requestedMode, AppSettings settings, int? fixedSeed = null)
    {
        MULTI_MONITOR_MODE = settings.SimulationMultiMonitorMode;
        long now = Stopwatch.GetTimestamp();
        _sharedSessionStartTicks = now;
        int delaySec = Math.Clamp(settings.SimulationStartDelaySeconds, 0, 30);
        _sharedCycleStartTicks = now + (long)(delaySec * Stopwatch.Frequency);
        _sharedRandomSeed = fixedSeed ?? Random.Shared.Next(1, int.MaxValue);

        if (requestedMode == SimulationModeType.RandomOSUpdate)
        {
            SimulationModeType[] osChoices =
            [
                SimulationModeType.Windows11Update,
                SimulationModeType.Windows10Update,
                SimulationModeType.MacOSUpdate,
                SimulationModeType.UbuntuUpdate,
                SimulationModeType.ChromeOSUpdate
            ];
            var seededRng = new Random(_sharedRandomSeed);
            _sharedEffectiveMode = osChoices[seededRng.Next(osChoices.Length)];
        }
        else
        {
            _sharedEffectiveMode = requestedMode;
        }

        _sharedIsBsodActive = false;
        _sharedBsodStartTicks = null;
        _sharedWasEnterDown = (NativeMethods.GetAsyncKeyState(0x0D) & 0x8000) != 0;
        _sharedWasEscapeDown = (NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0;
        _sharedWasSpaceDown = (NativeMethods.GetAsyncKeyState(0x20) & 0x8000) != 0;
        _sharedSessionActive = true;
    }

    public static void EndSharedSession()
    {
        _sharedSessionActive = false;
        _sharedIsBsodActive = false;
        _sharedBsodStartTicks = null;
        _sharedTelemetryTimer?.Stop();
        if (_sharedRenderingHooked)
        {
            CompositionTarget.Rendering -= OnSharedCompositionRendering;
            _sharedRenderingHooked = false;
        }
        SharedActiveControls.Clear();
    }

    private static void EnsureSharedTimersRunning(SimulationModeType mode)
    {
        if (_sharedTelemetryTimer == null)
        {
            _sharedTelemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _sharedTelemetryTimer.Tick += OnSharedTelemetryTick;
        }
        if (!_sharedTelemetryTimer.IsEnabled)
        {
            _sharedTelemetryTimer.Start();
        }

        if (mode != SimulationModeType.CyberOpsTerminal)
        {
            if (!_sharedRenderingHooked)
            {
                CompositionTarget.Rendering += OnSharedCompositionRendering;
                _sharedRenderingHooked = true;
            }
        }
    }

    public void RecomputeLayout()
    {
        if (_activeMode == SimulationModeType.CyberOpsTerminal)
        {
            _hackerDashboardControl.RecomputeLayout();
        }
        ApplyWindowsUpdateLayoutAndSizes();
        ApplyAllOsUpdateAndCrashLayouts();
    }

    private void LoadBrandLogoAssets()
    {
        try
        {
            string logosDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Logos");
            string ubuntuWordmarkPath = System.IO.Path.Combine(logosDir, "ubuntu_wordmark.png");
            if (File.Exists(ubuntuWordmarkPath))
            {
                ImgUbuntuWordmark.Source = new BitmapImage(new Uri(ubuntuWordmarkPath));
            }
        }
        catch { }
    }

    public void StartSimulation(SimulationModeType requestedMode, AppSettings settings, bool joinExistingSession = true)
    {
        StopSimulation();
        _settings = settings;
        MULTI_MONITOR_MODE = settings.SimulationMultiMonitorMode;

        if (!joinExistingSession || !_sharedSessionActive)
        {
            BeginSharedSession(requestedMode, settings);
        }

        if (!SharedActiveControls.Contains(this))
        {
            SharedActiveControls.Add(this);
        }

        _activeMode = _sharedEffectiveMode;
        _animTimeSec = GetSharedSessionElapsedSeconds();

        int delaySec = Math.Clamp(settings.SimulationStartDelaySeconds, 0, 30);
        double remainingDelaySec = Math.Max(0.0, delaySec - _animTimeSec);
        _remainingDelayTicks = (int)Math.Ceiling(remainingDelaySec * 4.0);
        if (_remainingDelayTicks > 0)
        {
            OverlayStartDelay.Visibility = Visibility.Visible;
            TxtStartDelayCountdown.Text = $"Starting in {(int)Math.Ceiling(remainingDelaySec)}";
        }
        else
        {
            OverlayStartDelay.Visibility = Visibility.Collapsed;
        }

        _currentPercent = Math.Clamp(settings.SimulationStartPercent, 0, 99);
        ViewModel.ApplySettings(settings, _activeMode);

        PanelCyberOps.Visibility = Visibility.Collapsed;
        PanelWindowsUpdate.Visibility = Visibility.Collapsed;
        PanelMacUpdate.Visibility = Visibility.Collapsed;
        PanelUbuntuUpdate.Visibility = Visibility.Collapsed;
        PanelChromeOSUpdate.Visibility = Visibility.Collapsed;
        PanelBsodPrank.Visibility = Visibility.Collapsed;

        if (_activeMode == SimulationModeType.CyberOpsTerminal)
        {
            PanelCyberOps.Visibility = Visibility.Visible;
            _hackerDashboardControl.Start(settings);
        }
        else
        {
            ConfigureOsUpdatePanel(_activeMode);
            ApplyNormalOsProgressUI(_currentPercent);
            EnsureWindowsSpinnerCompositionRunning(forceRestart: false);
            if (_sharedIsBsodActive)
            {
                TriggerBsodPrankInternal(resetSharedClock: false);
                UpdateBsodCrashRebootSequence();
            }
            else if (_remainingDelayTicks <= 0)
            {
                UpdateOsProgressState();
            }
        }

        EnsureSharedTimersRunning(_activeMode);
    }

    public void StopSimulation()
    {
        _hackerDashboardControl.Stop();
        SharedActiveControls.Remove(this);
        if (SharedActiveControls.Count == 0)
        {
            EndSharedSession();
        }
    }

    public bool HandleKeyDown(VirtualKey key)
    {
        if ((DateTime.UtcNow - _sharedLastKeyHandledUtc).TotalMilliseconds < 180)
            return true;

        if (key == VirtualKey.Escape)
            return false;

        if (_activeMode == SimulationModeType.CyberOpsTerminal)
        {
            // Real system monitor dashboard: any key press exits the screensaver
            return false;
        }

        if (key == VirtualKey.Enter && (_settings?.SimulationEnterTriggerBsod ?? true))
        {
            _sharedLastKeyHandledUtc = DateTime.UtcNow;
            if (!_sharedIsBsodActive)
            {
                _sharedIsBsodActive = true;
                _sharedBsodStartTicks = Stopwatch.GetTimestamp();
                foreach (var ctrl in SharedActiveControls.ToArray())
                {
                    ctrl.TriggerBsodPrankInternal(resetSharedClock: false);
                }
            }
            else
            {
                _sharedIsBsodActive = false;
                _sharedBsodStartTicks = null;
                foreach (var ctrl in SharedActiveControls.ToArray())
                {
                    ctrl.RestoreFromBsodPrankInternal();
                }
            }
            return true;
        }

        return false;
    }

    private static void PollSharedGlobalKeys()
    {
        if (SharedActiveControls.Count == 0) return;
        var primaryCtrl = SharedActiveControls[0];

        if (GetSharedSessionElapsedSeconds() < 0.4)
        {
            _sharedWasEnterDown = (NativeMethods.GetAsyncKeyState(0x0D) & 0x8000) != 0;
            _sharedWasEscapeDown = (NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0;
            _sharedWasSpaceDown = (NativeMethods.GetAsyncKeyState(0x20) & 0x8000) != 0;
            return;
        }

        bool isEnterDown = (NativeMethods.GetAsyncKeyState(0x0D) & 0x8000) != 0;
        if (isEnterDown && !_sharedWasEnterDown)
        {
            _sharedWasEnterDown = true;
            if (!primaryCtrl.HandleKeyDown(VirtualKey.Enter))
            {
                primaryCtrl.RequestDismiss?.Invoke();
            }
            return;
        }
        _sharedWasEnterDown = isEnterDown;

        bool isEscapeDown = (NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0;
        if (isEscapeDown && !_sharedWasEscapeDown)
        {
            _sharedWasEscapeDown = true;
            primaryCtrl.RequestDismiss?.Invoke();
            return;
        }
        _sharedWasEscapeDown = isEscapeDown;

        bool isSpaceDown = (NativeMethods.GetAsyncKeyState(0x20) & 0x8000) != 0;
        if (isSpaceDown && !_sharedWasSpaceDown)
        {
            _sharedWasSpaceDown = true;
            primaryCtrl.RequestDismiss?.Invoke();
            return;
        }
        _sharedWasSpaceDown = isSpaceDown;

        // Pressing any key (A-Z, 0-9) exits all windows together
        for (int vk = 0x30; vk <= 0x5A; vk++)
        {
            if ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                primaryCtrl.RequestDismiss?.Invoke();
                return;
            }
        }
    }

    private void RestoreFromBsodPrank()
    {
        _sharedIsBsodActive = false;
        _sharedBsodStartTicks = null;
        foreach (var ctrl in SharedActiveControls.ToArray())
        {
            ctrl.RestoreFromBsodPrankInternal();
        }
    }

    private void RestoreFromBsodPrankInternal()
    {
        _isWindowsBootPhase = false;
        _bsodStartedAtUtc = null;
        _bootPhaseStartUtc = null;
        PanelBsodPrank.Visibility = Visibility.Collapsed;
        ConfigureOsUpdatePanel(_activeMode);
        ApplyNormalOsProgressUI(_currentPercent);
    }

    private static int EvaluateIrregularBsodPercent(double elapsedSec)
    {
        double u = Math.Clamp(elapsedSec / BSOD_DUMP_DURATION_S, 0.0, 1.0);
        for (int i = 0; i < BSOD_IRREGULAR_STEPS.Length - 1; i++)
        {
            var (t0, p0) = BSOD_IRREGULAR_STEPS[i];
            var (t1, p1) = BSOD_IRREGULAR_STEPS[i + 1];
            if (u >= t0 && u <= t1)
            {
                if (p0 == p1 || t1 <= t0) return p0;
                double frac = (u - t0) / (t1 - t0);
                // Quantize to 5% steps for authentic crash dump jumps
                int raw = p0 + (int)Math.Round(frac * (p1 - p0));
                return Math.Clamp((raw / 5) * 5, 0, 100);
            }
        }
        return 100;
    }

    private void UpdateBsodCrashRebootSequence()
    {
        // macOS, Ubuntu, and ChromeOS crash screens are static halt/recovery screens
        if (_activeMode is SimulationModeType.MacOSUpdate or SimulationModeType.UbuntuUpdate or SimulationModeType.ChromeOSUpdate)
        {
            return;
        }

        _sharedBsodStartTicks ??= Stopwatch.GetTimestamp();
        double elapsedDumpSec = Math.Max(0.0, Stopwatch.GetElapsedTime(_sharedBsodStartTicks.Value).TotalSeconds * SPEED);

        if (elapsedDumpSec < BSOD_DUMP_DURATION_S)
        {
            _bsodPercent = EvaluateIrregularBsodPercent(elapsedDumpSec);
            ViewModel.BsodPercent = _bsodPercent;
            TxtBsodPercent.Text = ViewModel.BsodPercentText;
            TxtWin11BsodPercent.Text = ViewModel.BsodPercentText;
            return;
        }

        _bsodPercent = 100;
        ViewModel.BsodPercent = 100;
        double post100Sec = elapsedDumpSec - BSOD_DUMP_DURATION_S;

        if (post100Sec < BSOD_HOLD_100_S)
        {
            // Brief hold at "100% complete" before screen goes black
            TxtBsodPercent.Text = "100% complete";
            TxtWin11BsodPercent.Text = "100% complete";
        }
        else if (post100Sec < BSOD_HOLD_100_S + BLACK_SCREEN_S)
        {
            // 1.5-second pitch-black screen before Windows boot screen
            _isWindowsBootPhase = false;
            PanelBsodPrank.Visibility = Visibility.Visible;
            PanelWindowsUpdate.Visibility = Visibility.Collapsed;
            PanelBsodPrank.Background = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
            SubPanelWinBsod.Opacity = 0.0;
            SubPanelWin11BlackBsod.Opacity = 0.0;
        }
        else if (post100Sec < BSOD_HOLD_100_S + BLACK_SCREEN_S + BOOT_S)
        {
            // 6.0-second Windows Boot Screen: black background, logo at 40% H (fades in over 0.4s), spinner near bottom at 84% H
            double bootElapsed = post100Sec - (BSOD_HOLD_100_S + BLACK_SCREEN_S);
            if (!_isWindowsBootPhase)
            {
                _isWindowsBootPhase = true;
                _bootPhaseStartUtc = DateTime.UtcNow;
                PanelBsodPrank.Visibility = Visibility.Collapsed;
                PanelWindowsUpdate.Visibility = Visibility.Visible;
                PanelWindowsUpdate.Background = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));

                VectorWin11Logo.Visibility = _activeMode == SimulationModeType.Windows11Update ? Visibility.Visible : Visibility.Collapsed;
                VectorWin10Logo.Visibility = _activeMode == SimulationModeType.Windows10Update ? Visibility.Visible : Visibility.Collapsed;
                ContainerWinLogo.Visibility = Visibility.Visible;
                StackWinUpdateText.Visibility = Visibility.Collapsed;
                TxtWinBottomRestart.Visibility = Visibility.Collapsed;
                CanvasWinSpinner.Visibility = Visibility.Visible;

                EnsureWindowsSpinnerCompositionRunning(forceRestart: false);
                ApplyWindowsUpdateLayoutAndSizes();
            }

            ContainerWinLogo.Opacity = Math.Clamp(bootElapsed / BOOT_LOGO_FADE_IN_S, 0.0, 1.0);
        }
        else
        {
            // Boot complete -> loop back to Fake Update screen across all mirrored windows
            RestoreFromBsodPrank();
        }
    }

    private void ConfigureOsUpdatePanel(SimulationModeType mode)
    {
        _isWindowsBootPhase = false;
        _bootPhaseStartUtc = null;
        ContainerWinLogo.Visibility = Visibility.Collapsed;
        ContainerWinLogo.Opacity = 0.0;
        CanvasWinSpinner.Visibility = Visibility.Visible;
        StackWinUpdateText.Visibility = Visibility.Visible;
        TxtWinDontTurnOff.Visibility = Visibility.Visible;
        TxtWinBottomRestart.Visibility = Visibility.Visible;
        StackWinCenterContent.Opacity = 1.0;

        switch (mode)
        {
            case SimulationModeType.Windows11Update:
                // Full pitch-black background (#000000) for Windows 11 Update
                PanelWindowsUpdate.Background = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
                VectorWin11Logo.Visibility = Visibility.Visible;
                VectorWin10Logo.Visibility = Visibility.Collapsed;
                PanelWindowsUpdate.Visibility = Visibility.Visible;
                EnsureWindowsSpinnerCompositionRunning(forceRestart: true);
                break;

            case SimulationModeType.Windows10Update:
                // Official Windows 10 Update blue (#0078D7)
                PanelWindowsUpdate.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));
                VectorWin11Logo.Visibility = Visibility.Collapsed;
                VectorWin10Logo.Visibility = Visibility.Visible;
                PanelWindowsUpdate.Visibility = Visibility.Visible;
                EnsureWindowsSpinnerCompositionRunning(forceRestart: true);
                break;

            case SimulationModeType.MacOSUpdate:
                PanelMacUpdate.Background = new SolidColorBrush(MAC_UPDATE_BG_COLOR);
                PanelMacUpdate.Visibility = Visibility.Visible;
                ApplyAllOsUpdateAndCrashLayouts();
                break;

            case SimulationModeType.UbuntuUpdate:
                PanelUbuntuUpdate.Background = new SolidColorBrush(UBUNTU_UPDATE_BG_COLOR);
                PanelUbuntuUpdate.Visibility = Visibility.Visible;
                ApplyAllOsUpdateAndCrashLayouts();
                break;

            case SimulationModeType.ChromeOSUpdate:
                PanelChromeOSUpdate.Background = new SolidColorBrush(CHROME_BG_COLOR);
                PanelChromeOSUpdate.Visibility = Visibility.Visible;
                ApplyAllOsUpdateAndCrashLayouts();
                break;
        }
    }

    private void TriggerBsodPrank()
    {
        _sharedIsBsodActive = true;
        _sharedBsodStartTicks = Stopwatch.GetTimestamp();
        foreach (var ctrl in SharedActiveControls.ToArray())
        {
            ctrl.TriggerBsodPrankInternal(resetSharedClock: false);
        }
    }

    private void TriggerBsodPrankInternal(bool resetSharedClock)
    {
        if (resetSharedClock)
        {
            _sharedIsBsodActive = true;
            _sharedBsodStartTicks = Stopwatch.GetTimestamp();
        }
        _isWindowsBootPhase = false;
        _bsodPercent = 0;
        _bsodStartedAtUtc = DateTime.UtcNow;
        _bootPhaseStartUtc = null;
        ViewModel.BsodPercent = 0;

        if (_settings != null)
        {
            ViewModel.BsodStopCode = _settings.SimulationBsodStopCode;
            ViewModel.BsodFailedDriver = _settings.SimulationBsodFailedDriver;
            ViewModel.BsodMainMessage = _settings.SimulationBsodMainMessage;
            ViewModel.BsodSecondaryMessage = _settings.SimulationBsodSecondaryMessage;
        }

        PanelWindowsUpdate.Visibility = Visibility.Collapsed;
        PanelMacUpdate.Visibility = Visibility.Collapsed;
        PanelUbuntuUpdate.Visibility = Visibility.Collapsed;
        PanelChromeOSUpdate.Visibility = Visibility.Collapsed;
        PanelBsodPrank.Visibility = Visibility.Visible;

        SubPanelWinBsod.Visibility = Visibility.Collapsed;
        SubPanelWin11BlackBsod.Visibility = Visibility.Collapsed;
        SubPanelUnixPanic.Visibility = Visibility.Collapsed;
        SubPanelUbuntuPanic.Visibility = Visibility.Collapsed;
        SubPanelChromePanic.Visibility = Visibility.Collapsed;

        ApplyAllOsUpdateAndCrashLayouts();

        switch (_activeMode)
        {
            case SimulationModeType.MacOSUpdate:
                // macOS Multilingual Kernel Panic (#1A1A1A)
                PanelBsodPrank.Background = new SolidColorBrush(MAC_PANIC_BG_COLOR);
                SubPanelUnixPanic.Visibility = Visibility.Visible;
                SubPanelUnixPanic.Opacity = 1.0;
                break;

            case SimulationModeType.UbuntuUpdate:
                // Ubuntu Linux TTY Kernel Panic (#000000 pure console black)
                PanelBsodPrank.Background = new SolidColorBrush(UBUNTU_PANIC_BG_COLOR);
                SubPanelUbuntuPanic.Visibility = Visibility.Visible;
                SubPanelUbuntuPanic.Opacity = 1.0;
                break;

            case SimulationModeType.ChromeOSUpdate:
                // ChromeOS Recovery Screen (#FFFFFF white)
                PanelBsodPrank.Background = new SolidColorBrush(CHROME_BG_COLOR);
                SubPanelChromePanic.Visibility = Visibility.Visible;
                SubPanelChromePanic.Opacity = 1.0;
                break;

            case SimulationModeType.Windows11Update:
                // Windows 11 24H2+ Black Crash Screen (#000000, no :(, no QR, no URL)
                PanelBsodPrank.Background = new SolidColorBrush(WIN11_BSOD_BG_COLOR);
                SubPanelWin11BlackBsod.Visibility = Visibility.Visible;
                SubPanelWin11BlackBsod.Opacity = 1.0;

                TxtWin11BsodMain.Text = ViewModel.BsodMainMessage;
                TxtWin11BsodPercent.Text = ViewModel.BsodPercentText;
                TxtWin11BsodStopCode.Text = ViewModel.BsodStopCodeLine;
                TxtWin11BsodWhatFailed.Text = ViewModel.BsodWhatFailedLine;
                break;

            default:
                // Windows 10 Blue Crash Screen (#0078D7)
                PanelBsodPrank.Background = new SolidColorBrush(WIN10_BSOD_BG_COLOR);
                SubPanelWinBsod.Visibility = Visibility.Visible;
                SubPanelWinBsod.Opacity = 1.0;

                TxtBsodMainMessage.Text = FakeUpdateSimulationViewModel.DefaultBsodWin10Line1;
                TxtBsodSecondaryMessage.Text = FakeUpdateSimulationViewModel.DefaultBsodWin10Line2;
                TxtBsodStopCode.Text = ViewModel.BsodStopCodeLine;
                TxtBsodPercent.Text = ViewModel.BsodPercentText;
                break;
        }
    }

    private void BuildAuthenticBsodQrCode()
    {
        if (CanvasBsodQr == null) return;
        CanvasBsodQr.Children.Clear();

        // Version 2 (25x25 modules) QR Code inside 100x100 canvas (each module = 4x4px)
        const int size = 25;
        const double modPx = 4.0;
        var qrBrush = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));
        bool[,] modules = new bool[size, size];
        bool[,] reserved = new bool[size, size];

        void PlaceFinder(int r0, int c0)
        {
            for (int dr = -1; dr <= 7; dr++)
            {
                for (int dc = -1; dc <= 7; dc++)
                {
                    int r = r0 + dr, c = c0 + dc;
                    if (r < 0 || r >= size || c < 0 || c >= size) continue;
                    reserved[r, c] = true;
                    if (dr >= 0 && dr <= 6 && dc >= 0 && dc <= 6)
                    {
                        bool border = dr == 0 || dr == 6 || dc == 0 || dc == 6;
                        bool core = dr >= 2 && dr <= 4 && dc >= 2 && dc <= 4;
                        modules[r, c] = border || core;
                    }
                }
            }
        }

        PlaceFinder(0, 0);
        PlaceFinder(0, size - 7);
        PlaceFinder(size - 7, 0);

        // 5x5 Alignment Pattern at (16,16)
        for (int dr = -2; dr <= 2; dr++)
        {
            for (int dc = -2; dc <= 2; dc++)
            {
                int r = 18 + dr, c = 18 + dc;
                reserved[r, c] = true;
                bool border = Math.Abs(dr) == 2 || Math.Abs(dc) == 2;
                bool center = dr == 0 && dc == 0;
                modules[r, c] = border || center;
            }
        }

        // Timing patterns along row 6 and col 6
        for (int i = 8; i < size - 8; i++)
        {
            reserved[6, i] = true;
            reserved[i, 6] = true;
            modules[6, i] = (i % 2 == 0);
            modules[i, 6] = (i % 2 == 0);
        }

        // Deterministic authentic bit pattern for https://www.windows.com/stopcode
        uint seed = 0x57494E31;
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (reserved[r, c]) continue;
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                modules[r, c] = ((seed + (uint)(r * 31 + c * 17)) & 1) == 0;
            }
        }

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (!modules[r, c]) continue;
                var rect = new Rectangle
                {
                    Width = modPx + 0.15,
                    Height = modPx + 0.15,
                    Fill = qrBrush
                };
                Canvas.SetLeft(rect, c * modPx);
                Canvas.SetTop(rect, r * modPx);
                CanvasBsodQr.Children.Add(rect);
            }
        }
    }

    private void UpdateOsProgressState()
    {
        if (_settings == null || _sharedIsBsodActive) return;

        int startPct = Math.Clamp(_settings.SimulationStartPercent, 0, 99);
        double totalSeconds = Math.Max(1, _settings.SimulationDurationMinutes) * 60.0;
        double elapsedSeconds = GetSharedCycleElapsedSeconds();
        double fraction = Math.Clamp(elapsedSeconds / totalSeconds, 0.0, 1.0);

        int computedPct = ViewModel.ComputeRealisticPercentage(fraction, _sharedRandomSeed);

        if (_settings.SimulationWhenFinished == SimulationFinishAction.HoldAt99)
        {
            _currentPercent = Math.Min(99, computedPct);
            ApplyNormalOsProgressUI(_currentPercent);
            return;
        }

        if (fraction < 1.0)
        {
            _currentPercent = Math.Min(99, computedPct);
            ApplyNormalOsProgressUI(_currentPercent);
            return;
        }

        _currentPercent = 100;

        switch (_settings.SimulationWhenFinished)
        {
            case SimulationFinishAction.DismissScreensaver:
                RequestDismiss?.Invoke();
                break;

            case SimulationFinishAction.HoldAt100:
                ApplyNormalOsProgressUI(100);
                break;

            case SimulationFinishAction.RebootLoop:
            default:
                double postFinishSec = Math.Max(0.0, (elapsedSeconds - totalSeconds) * SPEED);
                if (postFinishSec < RESTARTING_SCREEN_S)
                {
                    // "Restarting" screen before screen goes black: spinner at 35.9% H, "Restarting" centered below it at 43.4% H
                    if (_isWindowsBootPhase)
                    {
                        _isWindowsBootPhase = false;
                        ApplyWindowsUpdateLayoutAndSizes();
                    }
                    ContainerWinLogo.Visibility = Visibility.Collapsed;
                    CanvasWinSpinner.Visibility = Visibility.Visible;
                    StackWinUpdateText.Visibility = Visibility.Visible;
                    TxtWinUpdatePercent.Text = "Restarting";
                    TxtWinDontTurnOff.Visibility = Visibility.Collapsed;
                    TxtWinBottomRestart.Visibility = Visibility.Collapsed;
                    TxtChromeTitle.Text = "Restarting...";
                }
                else if (postFinishSec < RESTARTING_SCREEN_S + BLACK_SCREEN_S)
                {
                    // 1.5s black screen before boot screen
                    _isWindowsBootPhase = false;
                    ContainerWinLogo.Visibility = Visibility.Collapsed;
                    StackWinUpdateText.Visibility = Visibility.Collapsed;
                    TxtWinBottomRestart.Visibility = Visibility.Collapsed;
                    CanvasWinSpinner.Visibility = Visibility.Collapsed;
                    PanelWindowsUpdate.Background = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
                }
                else if (postFinishSec < RESTARTING_SCREEN_S + BLACK_SCREEN_S + BOOT_S)
                {
                    // 6.0s Windows Boot Screen: black background, logo at 40% H (fades in over 0.4s), spinner near bottom at 84% H
                    double bootElapsed = postFinishSec - (RESTARTING_SCREEN_S + BLACK_SCREEN_S);
                    if (!_isWindowsBootPhase)
                    {
                        _isWindowsBootPhase = true;
                        _bootPhaseStartUtc = DateTime.UtcNow;
                        PanelWindowsUpdate.Background = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
                        VectorWin11Logo.Visibility = _activeMode == SimulationModeType.Windows11Update ? Visibility.Visible : Visibility.Collapsed;
                        VectorWin10Logo.Visibility = _activeMode == SimulationModeType.Windows10Update ? Visibility.Visible : Visibility.Collapsed;
                        ContainerWinLogo.Visibility = Visibility.Visible;
                        StackWinUpdateText.Visibility = Visibility.Collapsed;
                        TxtWinBottomRestart.Visibility = Visibility.Collapsed;
                        CanvasWinSpinner.Visibility = Visibility.Visible;
                        EnsureWindowsSpinnerCompositionRunning(forceRestart: false);
                        ApplyWindowsUpdateLayoutAndSizes();
                    }
                    ContainerWinLogo.Opacity = Math.Clamp(bootElapsed / BOOT_LOGO_FADE_IN_S, 0.0, 1.0);
                }
                else
                {
                    // Advance shared cycle start timestamp once and reset all mirrored controls together
                    _sharedCycleStartTicks = Stopwatch.GetTimestamp();
                    foreach (var ctrl in SharedActiveControls.ToArray())
                    {
                        ctrl._isWindowsBootPhase = false;
                        ctrl._bootPhaseStartUtc = null;
                        ctrl.ConfigureOsUpdatePanel(ctrl._activeMode);
                        ctrl.ApplyNormalOsProgressUI(startPct);
                    }
                }
                break;
        }
    }

    private void ApplyNormalOsProgressUI(int pct)
    {
        ViewModel.ActiveMode = _activeMode;
        if (_settings != null)
        {
            ViewModel.CustomUpdateLine1 = _settings.SimulationCustomUpdateLine1;
            ViewModel.CustomUpdateLine2 = _settings.SimulationCustomUpdateLine2;
            ViewModel.CustomUpdateBottomText = _settings.SimulationCustomUpdateBottomText;
        }
        ViewModel.CurrentPercent = pct;

        TxtWinUpdatePercent.Text = ViewModel.UpdatePrimaryText;
        TxtWinDontTurnOff.Text = ViewModel.UpdateSecondaryText;
        TxtWinBottomRestart.Text = ViewModel.UpdateBottomRestartText;

        TxtWinDontTurnOff.Visibility = Visibility.Visible;
        TxtWinBottomRestart.Visibility = Visibility.Visible;
        StackWinUpdateText.Visibility = Visibility.Visible;

        double screenW = ActualWidth > 200 ? ActualWidth : 1365.0;
        double macTrackWidth = screenW * MAC_BAR_WIDTH_FRAC;
        double macFillFraction = Math.Clamp(pct / 100.0, 0.03, 1.0);
        BarMacProgressFill.Width = Math.Clamp(macTrackWidth * macFillFraction, 0.0, macTrackWidth);

        double chromeTrackWidth = screenW * CHROME_BAR_WIDTH_W_FRAC;
        double chromeFillFraction = Math.Clamp(pct / 100.0, 0.03, 1.0);
        BarChromeProgressFill.Width = Math.Clamp(chromeTrackWidth * chromeFillFraction, 0.0, chromeTrackWidth);

        TxtChromeTitle.Text = "Updating your device";
    }

    // =========================================================================
    // MEASURED PCHIP WINDOWS 10 / 11 SPINNER & EXACT HEIGHT-SCALED LAYOUT
    // =========================================================================

    private static double[] ComputePeriodicPchipSlopes()
    {
        // N = 40 intervals over [0, 4.0s] with period = 720 degrees per loop
        int n = LEAD_KEYFRAMES.Length - 1; // 40
        double h = SAMPLE_STEP_S;
        double[] delta = new double[n];

        for (int k = 0; k < n; k++)
        {
            double y0 = LEAD_KEYFRAMES[k];
            // Wrap seamlessly at loop boundary so f(4.0) = f(0) + 720 = 733
            double y1 = (k == n - 1) ? (LEAD_KEYFRAMES[0] + 720.0) : LEAD_KEYFRAMES[k + 1];
            delta[k] = (y1 - y0) / h;
        }

        double[] slopes = new double[n];
        for (int k = 0; k < n; k++)
        {
            double dPrev = delta[(k - 1 + n) % n];
            double dCurr = delta[k];
            if (dPrev * dCurr <= 0.0)
            {
                slopes[k] = 0.0;
            }
            else
            {
                // Harmonic mean for equal step size h (Fritsch-Carlson monotone cubic PCHIP)
                slopes[k] = (2.0 * dPrev * dCurr) / (dPrev + dCurr);
            }
        }

        return slopes;
    }

    private static double EvaluatePeriodicLeadCurve(double tau)
    {
        // Periodic unwrapped curve: f(tau + 4.0) = f(tau) + 720
        double loops = Math.Floor(tau / LOOP_S);
        double u = tau - loops * LOOP_S; // u in [0, 4.0)
        if (u < 0.0) u += LOOP_S;

        int n = LEAD_KEYFRAMES.Length - 1; // 40
        double scaled = u / SAMPLE_STEP_S;
        int idx = (int)Math.Floor(scaled);
        if (idx >= n) idx = n - 1;
        double s = scaled - idx; // local fraction in [0, 1)

        double y0 = LEAD_KEYFRAMES[idx];
        double y1 = (idx == n - 1) ? (LEAD_KEYFRAMES[0] + 720.0) : LEAD_KEYFRAMES[idx + 1];
        double m0 = _pchipSlopes[idx] * SAMPLE_STEP_S;
        double m1 = _pchipSlopes[(idx + 1) % n] * SAMPLE_STEP_S;

        double s2 = s * s;
        double s3 = s2 * s;
        double h00 = 2.0 * s3 - 3.0 * s2 + 1.0;
        double h10 = s3 - 2.0 * s2 + s;
        double h01 = -2.0 * s3 + 3.0 * s2;
        double h11 = s3 - s2;

        double fU = h00 * y0 + h10 * m0 + h01 * y1 + h11 * m1;
        return fU + 720.0 * loops;
    }

    private void OnSimulationControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyWindowsUpdateLayoutAndSizes();
        ApplyAllOsUpdateAndCrashLayouts();
    }

    private void BuildUbuntuPanicConsoleLines()
    {
        if (SubPanelUbuntuPanic == null) return;
        SubPanelUbuntuPanic.Children.Clear();
        _ubuntuPanicTextBlocks.Clear();

        var brush = new SolidColorBrush(UBUNTU_PANIC_TEXT_COLOR);
        var font = new FontFamily("Consolas, Cascadia Mono, Courier New");

        for (int i = 0; i < UBUNTU_PANIC_LINES.Length; i++)
        {
            var tb = new TextBlock
            {
                Text = UBUNTU_PANIC_LINES[i],
                FontFamily = font,
                Foreground = brush,
                FontWeight = FontWeights.Normal,
                IsTextScaleFactorEnabled = false
            };
            SubPanelUbuntuPanic.Children.Add(tb);
            _ubuntuPanicTextBlocks.Add(tb);
        }
    }

    private void ApplyAllOsUpdateAndCrashLayouts()
    {
        double screenW = ActualWidth > 200 ? ActualWidth : 1365.0;
        double screenH = ActualHeight > 120 ? ActualHeight : 767.0;

        // 1. macOS (A) Boot/Update Screen Layout
        if (ViewboxMacLogo != null && BarMacProgressTrack != null)
        {
            double logoH = screenH * MAC_LOGO_HEIGHT_FRAC;
            // Preserve exact 111:137 Apple logo aspect ratio on all aspect ratios (matches 8.13% W at 16:9)
            double logoW = logoH * (111.0 / 137.0);
            ViewboxMacLogo.Width = logoW;
            ViewboxMacLogo.Height = logoH;
            ViewboxMacLogo.Margin = new Thickness(0, (screenH * MAC_LOGO_CENTER_Y_FRAC) - (logoH * 0.5), 0, 0);

            double barW = screenW * MAC_BAR_WIDTH_FRAC;
            double barH = Math.Max(2.0, screenH * MAC_BAR_HEIGHT_FRAC);
            BarMacProgressTrack.Width = barW;
            BarMacProgressTrack.Height = barH;
            BarMacProgressTrack.CornerRadius = new CornerRadius(barH * 0.5);
            BarMacProgressTrack.Margin = new Thickness(0, (screenH * MAC_BAR_CENTER_Y_FRAC) - (barH * 0.5), 0, 0);

            BarMacProgressFill.Height = barH;
            BarMacProgressFill.CornerRadius = new CornerRadius(barH * 0.5);
            double macFillFrac = Math.Clamp(_currentPercent / 100.0, 0.03, 1.0);
            BarMacProgressFill.Width = barW * macFillFrac;
        }

        // 2. macOS (B) Kernel Panic Crash Screen Layout
        if (TxtMacPanicEn1 != null)
        {
            TextBlock[] macLines =
            [
                TxtMacPanicEn1, TxtMacPanicEn2,
                TxtMacPanicFr1, TxtMacPanicFr2,
                TxtMacPanicDe1, TxtMacPanicDe2,
                TxtMacPanicJa1, TxtMacPanicJa2,
                TxtMacPanicEs1, TxtMacPanicEs2
            ];
            double macFontPx = Math.Min(screenH * MAC_PANIC_FONT_HEIGHT_FRAC, (screenW * 0.92) / 38.0);
            double halfLineH = macFontPx * 0.665;
            for (int i = 0; i < macLines.Length; i++)
            {
                macLines[i].FontSize = macFontPx;
                macLines[i].Margin = new Thickness(0, (screenH * MAC_PANIC_LINE_CENTER_Y_FRACS[i]) - halfLineH, 0, 0);
            }
        }

        // 3. Ubuntu (A) Boot/Update Screen Layout
        if (ImgUbuntuWordmark != null && UbuntuDot0 != null)
        {
            // Preserve exact 314:66 wordmark aspect ratio and center horizontally (1:1 match at 1365x767)
            double visH = screenH * UBUNTU_WORDMARK_H_FRAC;
            double visW = visH * (314.0 / 66.0);
            double visX = (screenW * 0.5) - (visW * 0.5) + (screenH * (13.5 / 767.0));
            double visY = screenH * UBUNTU_WORDMARK_Y_FRAC;

            double imgW = visW * (714.0 / 700.0);
            double imgH = visH * (222.0 / 152.0);
            double imgLeft = visX - (14.0 / 714.0) * imgW;
            double imgTop = visY - (31.0 / 222.0) * imgH;

            ImgUbuntuWordmark.Width = imgW;
            ImgUbuntuWordmark.Height = imgH;
            Canvas.SetLeft(ImgUbuntuWordmark, imgLeft);
            Canvas.SetTop(ImgUbuntuWordmark, imgTop);

            double dotDiam = screenH * UBUNTU_DOT_DIAMETER_H_FRAC;
            double dotTop = (screenH * UBUNTU_DOT_CENTER_Y_FRAC) - (dotDiam * 0.5);
            double dotSpacing = screenH * (44.0 / 767.0);
            double centerDotX = (screenW * 0.5) + (screenH * (0.5 / 767.0));
            Ellipse[] ubDots = [UbuntuDot0, UbuntuDot1, UbuntuDot2, UbuntuDot3, UbuntuDot4];
            for (int i = 0; i < ubDots.Length; i++)
            {
                ubDots[i].Width = dotDiam;
                ubDots[i].Height = dotDiam;
                double dotX = centerDotX + ((i - 2) * dotSpacing);
                Canvas.SetLeft(ubDots[i], dotX - (dotDiam * 0.5));
                Canvas.SetTop(ubDots[i], dotTop);
            }
        }

        // 4. Ubuntu (B) Kernel Panic Console Layout
        if (_ubuntuPanicTextBlocks.Count == UBUNTU_PANIC_LINES.Length)
        {
            double ubFontPx = Math.Min(screenH * UBUNTU_PANIC_FONT_H_FRAC, (screenW * 0.96) / 62.0);
            double halfLineH = ubFontPx * 0.62;
            double leftPx = screenW * UBUNTU_PANIC_LEFT_X_FRAC;
            for (int i = 0; i < _ubuntuPanicTextBlocks.Count; i++)
            {
                var tb = _ubuntuPanicTextBlocks[i];
                tb.FontSize = ubFontPx;
                double centerY = screenH * (UBUNTU_PANIC_FIRST_CENTER_Y_FRAC + i * UBUNTU_PANIC_LINE_STEP_Y_FRAC);
                Canvas.SetLeft(tb, leftPx);
                Canvas.SetTop(tb, centerY - halfLineH);
            }
        }

        // 5. ChromeOS (A) Boot/Update Screen Layout
        if (CanvasChromeSpinner != null && TxtChromeTitle != null && BarChromeProgressTrack != null)
        {
            double spinDiam = screenH * CHROME_SPINNER_DIAM_H_FRAC;
            CanvasChromeSpinner.Width = spinDiam;
            CanvasChromeSpinner.Height = spinDiam;
            CanvasChromeSpinner.Margin = new Thickness(0, (screenH * CHROME_SPINNER_CENTER_Y_FRAC) - (spinDiam * 0.5), 0, 0);
            PathChromeSpinnerArc.StrokeThickness = screenH * CHROME_SPINNER_STROKE_H_FRAC;

            double titleFont = screenH * CHROME_UPD_TITLE_FONT_H_FRAC;
            TxtChromeTitle.FontSize = titleFont;
            TxtChromeTitle.Margin = new Thickness(0, (screenH * CHROME_UPD_TITLE_CENTER_Y_FRAC) - (titleFont * 0.665), 0, 0);

            double subFont = screenH * CHROME_UPD_SUB_FONT_H_FRAC;
            TxtChromeSub.FontSize = subFont;
            TxtChromeSub.Margin = new Thickness(0, (screenH * CHROME_UPD_SUB_CENTER_Y_FRAC) - (subFont * 0.665), 0, 0);

            double barW = screenW * CHROME_BAR_WIDTH_W_FRAC;
            double barH = Math.Max(2.0, screenH * CHROME_BAR_HEIGHT_H_FRAC);
            BarChromeProgressTrack.Width = barW;
            BarChromeProgressTrack.Height = barH;
            BarChromeProgressTrack.CornerRadius = new CornerRadius(barH * 0.5);
            BarChromeProgressTrack.Margin = new Thickness(0, (screenH * CHROME_BAR_CENTER_Y_FRAC) - (barH * 0.5), 0, 0);

            BarChromeProgressFill.Height = barH;
            BarChromeProgressFill.CornerRadius = new CornerRadius(barH * 0.5);
            double chromeFillFrac = Math.Clamp(_currentPercent / 100.0, 0.03, 1.0);
            BarChromeProgressFill.Width = barW * chromeFillFrac;

            UpdateChromeSpinnerFrame();
        }

        // 6. ChromeOS (B) Recovery Crash Screen Layout
        if (GridChromeCrashIcon != null && TxtChromeCrashTitle != null && TxtChromeCrashSub != null)
        {
            double iconDiam = screenH * CHROME_CRASH_ICON_DIAM_H_FRAC;
            GridChromeCrashIcon.Width = iconDiam;
            GridChromeCrashIcon.Height = iconDiam;
            GridChromeCrashIcon.Margin = new Thickness(0, (screenH * CHROME_CRASH_ICON_CENTER_Y_FRAC) - (iconDiam * 0.5), 0, 0);

            // Exclamation stem & dot inside 53x53 reference circle:
            // stem: cols 25..28 (w=4/53), rows 14..28 (top=14/53, h=15/53)
            // dot: cols 24..28 (w=5/53), rows 35..38 (top=35/53, h=4.5/53)
            double stemW = iconDiam * (4.5 / 53.0);
            double stemH = iconDiam * (15.5 / 53.0);
            RectChromeExclStem.Width = stemW;
            RectChromeExclStem.Height = stemH;
            Canvas.SetLeft(RectChromeExclStem, (iconDiam - stemW) * 0.5);
            Canvas.SetTop(RectChromeExclStem, iconDiam * (13.8 / 53.0));

            double dotD = iconDiam * (5.0 / 53.0);
            EllipseChromeExclDot.Width = dotD;
            EllipseChromeExclDot.Height = dotD;
            Canvas.SetLeft(EllipseChromeExclDot, (iconDiam - dotD) * 0.5);
            Canvas.SetTop(EllipseChromeExclDot, iconDiam * (34.5 / 53.0));

            double cTitleFont = screenH * CHROME_CRASH_TITLE_FONT_H_FRAC;
            TxtChromeCrashTitle.FontSize = cTitleFont;
            TxtChromeCrashTitle.Margin = new Thickness(0, (screenH * CHROME_CRASH_TITLE_CENTER_Y_FRAC) - (cTitleFont * 0.665), 0, 0);

            double cSubFont = screenH * CHROME_CRASH_SUB_FONT_H_FRAC;
            TxtChromeCrashSub.FontSize = cSubFont;
            TxtChromeCrashSub.Margin = new Thickness(0, (screenH * CHROME_CRASH_SUB_CENTER_Y_FRAC) - (cSubFont * 0.665), 0, 0);
        }

        // 7. Windows 10 Blue Crash Screen (#0078D7) Layout
        if (TxtWin10BsodSadFace != null && BorderWin10BsodQr != null)
        {
            double leftX = screenW * WIN10_BSOD_LEFT_X_FRAC;

            double sadFont = screenH * WIN10_BSOD_SAD_FONT_H_FRAC;
            TxtWin10BsodSadFace.FontSize = sadFont;
            TxtWin10BsodSadFace.Margin = new Thickness(leftX, screenH * WIN10_BSOD_SAD_TOP_Y_FRAC, 0, 0);

            double mainFont = Math.Min(screenH * WIN10_BSOD_MAIN_FONT_H_FRAC, (screenW * 0.80) / 31.0);
            TxtBsodMainMessage.FontSize = mainFont;
            TxtBsodMainMessage.Margin = new Thickness(leftX, screenH * WIN10_BSOD_LINE1_TOP_Y_FRAC, 0, 0);

            TxtBsodSecondaryMessage.FontSize = mainFont;
            TxtBsodSecondaryMessage.Margin = new Thickness(leftX, screenH * WIN10_BSOD_LINE2_TOP_Y_FRAC, 0, 0);

            TxtBsodPercent.FontSize = mainFont;
            TxtBsodPercent.Margin = new Thickness(leftX, screenH * WIN10_BSOD_PCT_TOP_Y_FRAC, 0, 0);

            double qrSize = screenH * WIN10_BSOD_QR_SIZE_H_FRAC;
            double qrPad = Math.Max(3.0, qrSize * 0.06);
            BorderWin10BsodQr.Width = qrSize;
            BorderWin10BsodQr.Height = qrSize;
            BorderWin10BsodQr.Padding = new Thickness(qrPad);
            BorderWin10BsodQr.Margin = new Thickness(leftX, screenH * WIN10_BSOD_QR_TOP_Y_FRAC, 0, 0);

            double infoLeftX = leftX + qrSize + (screenW * WIN10_BSOD_INFO_GAP_W_FRAC);
            double smallFont = Math.Min(screenH * WIN10_BSOD_SMALL_FONT_H_FRAC, Math.Max(8.0, (screenW - infoLeftX - 16.0) / 48.0));

            TxtWin10BsodUrl.FontSize = smallFont;
            TxtWin10BsodUrl.Margin = new Thickness(infoLeftX, screenH * WIN10_BSOD_URL_TOP_Y_FRAC, 0, 0);

            TxtWin10BsodSupport.FontSize = smallFont;
            TxtWin10BsodSupport.Margin = new Thickness(infoLeftX, screenH * WIN10_BSOD_SUPPORT_TOP_Y_FRAC, 0, 0);

            TxtBsodStopCode.FontSize = smallFont;
            TxtBsodStopCode.Margin = new Thickness(infoLeftX, screenH * WIN10_BSOD_STOP_TOP_Y_FRAC, 0, 0);
        }

        // 8. Windows 11 24H2+ Black Crash Screen (#000000) Layout
        if (TxtWin11BsodMain != null && TxtWin11BsodStopCode != null)
        {
            double mainFont11 = Math.Min(screenH * WIN11_BSOD_MAIN_FONT_H_FRAC, (screenW * 0.88) / 26.0);
            double halfLine11 = mainFont11 * 0.665;

            TxtWin11BsodMain.FontSize = mainFont11;
            TxtWin11BsodMain.Margin = new Thickness(0, (screenH * WIN11_BSOD_MAIN_CENTER_Y_FRAC) - halfLine11, 0, 0);

            TxtWin11BsodPercent.FontSize = mainFont11;
            TxtWin11BsodPercent.Margin = new Thickness(0, (screenH * WIN11_BSOD_PCT_CENTER_Y_FRAC) - halfLine11, 0, 0);

            double bottomLeftX = screenW * WIN11_BSOD_BOTTOM_LEFT_X_FRAC;
            double smallFont11 = screenH * WIN11_BSOD_SMALL_FONT_H_FRAC;

            TxtWin11BsodStopCode.FontSize = smallFont11;
            TxtWin11BsodStopCode.Margin = new Thickness(bottomLeftX, screenH * WIN11_BSOD_STOP_TOP_Y_FRAC, 0, 0);

            TxtWin11BsodWhatFailed.FontSize = smallFont11;
            TxtWin11BsodWhatFailed.Margin = new Thickness(bottomLeftX, screenH * WIN11_BSOD_FAILED_TOP_Y_FRAC, 0, 0);
        }
    }

    private (double screenH, double ringPx, double dotPx, double fontPx) ComputeSpinnerDimensionsFromScreen()
    {
        double screenW = ActualWidth > 200 ? ActualWidth : 1365.0;
        double screenH = ActualHeight > 120 ? ActualHeight : 767.0;
        double ringPx = screenH * RING_DIAMETER_HEIGHT_FRAC;
        double dotPx = screenH * DOT_DIAMETER_HEIGHT_FRAC;
        // Use monitor's own height for font size, capped only on ultra-narrow portrait screens so text stays centered without clipping
        double fontPx = Math.Min(screenH * FONT_SIZE_HEIGHT_FRAC, (screenW * 0.90) / 23.0);
        return (screenH, ringPx, dotPx, fontPx);
    }

    private void ApplyWindowsUpdateLayoutAndSizes()
    {
        if (CanvasWinSpinner == null || TxtWinUpdatePercent == null) return;

        var (screenH, ringPx, dotPx, fontPx) = ComputeSpinnerDimensionsFromScreen();

        // Windows Boot Logo: height = 12% of screen height, centered at y = 40% of screen height
        if (ContainerWinLogo != null)
        {
            double logoSize = screenH * BOOT_LOGO_SIZE_H_FRAC;
            VectorWin11Logo.Width = logoSize;
            VectorWin11Logo.Height = logoSize;
            VectorWin10Logo.Width = logoSize;
            VectorWin10Logo.Height = logoSize;
            ContainerWinLogo.Margin = new Thickness(0, (screenH * BOOT_LOGO_CENTER_Y_FRAC) - (logoSize * 0.5), 0, 0);
        }

        // Ring size and vertical center: 35.9% H on update screen, 76.0% H on boot screen
        double spinnerCenterYFrac = _isWindowsBootPhase ? BOOT_SPINNER_CENTER_Y_FRAC : RING_CENTER_Y_FRAC;
        CanvasWinSpinner.Width = ringPx;
        CanvasWinSpinner.Height = ringPx;
        CanvasWinSpinner.Margin = new Thickness(0, (screenH * spinnerCenterYFrac) - (ringPx * 0.5), 0, 0);

        for (int i = 0; i < _winSpinnerEllipses.Count; i++)
        {
            _winSpinnerEllipses[i].Width = dotPx;
            _winSpinnerEllipses[i].Height = dotPx;
        }

        // Font size = 2.7% of screen height (~20.7px at 767px height)
        TxtWinUpdatePercent.FontSize = fontPx;
        TxtWinDontTurnOff.FontSize = fontPx;
        TxtWinBottomRestart.FontSize = fontPx;

        // Line height of Segoe UI is ~1.33 * fontPx; center each line at 43.4%, 48.0%, 88.0% of screenH
        double halfLineH = fontPx * 0.665;
        TxtWinUpdatePercent.Margin = new Thickness(0, (screenH * LINE1_CENTER_Y_FRAC) - halfLineH, 0, 0);
        TxtWinDontTurnOff.Margin = new Thickness(0, (screenH * LINE2_CENTER_Y_FRAC) - halfLineH, 0, 0);
        TxtWinBottomRestart.Margin = new Thickness(0, (screenH * BOTTOM_LINE_CENTER_Y_FRAC) - halfLineH, 0, 0);

        UpdateWindowsSpinnerFrame();
    }

    private void BuildWindowsSpinnerDots()
    {
        CanvasWinSpinner.Children.Clear();
        _winSpinnerEllipses.Clear();
        _winSpinnerDotVisuals.Clear();

        var (_, ringPx, dotPx, _) = ComputeSpinnerDimensionsFromScreen();
        CanvasWinSpinner.Width = ringPx;
        CanvasWinSpinner.Height = ringPx;

        for (int j = 0; j < DOT_COUNT; j++)
        {
            var dot = new Ellipse
            {
                Width = dotPx,
                Height = dotPx,
                Fill = BrushWhite,
                Opacity = 1.0,
                IsHitTestVisible = false
            };

            CanvasWinSpinner.Children.Add(dot);
            _winSpinnerEllipses.Add(dot);
            _winSpinnerDotVisuals.Add(ElementCompositionPreview.GetElementVisual(dot));
        }

        ApplyWindowsUpdateLayoutAndSizes();
    }

    private void EnsureWindowsSpinnerCompositionRunning(bool forceRestart = false)
    {
        if (_winSpinnerEllipses.Count == 0)
        {
            BuildWindowsSpinnerDots();
        }

        ApplyWindowsUpdateLayoutAndSizes();

        if (!_sharedRenderingHooked)
        {
            CompositionTarget.Rendering += OnSharedCompositionRendering;
            _sharedRenderingHooked = true;
        }

        UpdateWindowsSpinnerFrame();
    }

    private static void OnSharedCompositionRendering(object? sender, object e)
    {
        if (SharedActiveControls.Count == 0) return;

        // Compute elapsed time once from the shared clock so all mirrored windows render the exact same phase
        double sharedElapsedSec = GetSharedSessionElapsedSeconds();

        for (int i = 0; i < SharedActiveControls.Count; i++)
        {
            var ctrl = SharedActiveControls[i];
            if (_sharedIsBsodActive && !ctrl._isWindowsBootPhase) continue;

            if (ctrl._activeMode is SimulationModeType.Windows11Update or SimulationModeType.Windows10Update)
            {
                ctrl.UpdateWindowsSpinnerFrame(sharedElapsedSec);
            }
            else if (ctrl._activeMode == SimulationModeType.UbuntuUpdate)
            {
                ctrl.UpdateUbuntuDotsAnimation(sharedElapsedSec);
            }
            else if (ctrl._activeMode == SimulationModeType.ChromeOSUpdate)
            {
                ctrl.UpdateChromeSpinnerFrame(sharedElapsedSec);
            }
        }
    }

    private void UpdateWindowsSpinnerFrame(double? sharedElapsedOverride = null)
    {
        if (_winSpinnerDotVisuals.Count < DOT_COUNT) return;

        var (_, ringPx, dotPx, _) = ComputeSpinnerDimensionsFromScreen();
        double radius = (ringPx - dotPx) * 0.5;
        double center = (ringPx - dotPx) * 0.5;

        double t = (sharedElapsedOverride ?? GetSharedSessionElapsedSeconds()) * SPEED;

        for (int j = 0; j < DOT_COUNT; j++)
        {
            double tau = t - (STAGGER_S * j);
            double unwrappedDeg = EvaluatePeriodicLeadCurve(tau) - (DOT_EXTRA_OFFSET_DEG * j);
            double angleDeg = unwrappedDeg % 360.0;
            if (angleDeg < 0.0) angleDeg += 360.0;

            double rad = angleDeg * (Math.PI / 180.0);
            float x = (float)(center + radius * Math.Sin(rad));
            float y = (float)(center - radius * Math.Cos(rad));

            _winSpinnerDotVisuals[j].Offset = new Vector3(x, y, 0.0f);
        }
    }

    private void UpdateUbuntuDotsAnimation(double? sharedElapsedOverride = null)
    {
        // [UNVERIFIED PLACEHOLDER TIMING]: Sequential fade on #FC3D04 dots, 1.2s cycle with 150ms stagger
        if (UbuntuDot0 == null) return;
        double t = (sharedElapsedOverride ?? GetSharedSessionElapsedSeconds()) * SPEED;
        Ellipse[] dots = [UbuntuDot0, UbuntuDot1, UbuntuDot2, UbuntuDot3, UbuntuDot4];

        for (int i = 0; i < dots.Length; i++)
        {
            double local = (t - i * UBUNTU_DOT_STAGGER_S) % UBUNTU_DOT_CYCLE_S;
            if (local < 0) local += UBUNTU_DOT_CYCLE_S;
            double phase = local / UBUNTU_DOT_CYCLE_S; // 0..1

            // Smooth cosine pulse between 0.35 and 1.0 opacity while keeping #FC3D04 fill
            double wave = 0.5 * (1.0 + Math.Cos(phase * 2.0 * Math.PI));
            dots[i].Fill = BrushUbuntuOrange;
            dots[i].Opacity = 0.35 + 0.65 * wave;
        }
    }

    private void UpdateChromeSpinnerFrame(double? sharedElapsedOverride = null)
    {
        // [UNVERIFIED PLACEHOLDER TIMING]: Material indeterminate rotating/expanding arc (~1.4s per cycle)
        if (PathChromeSpinnerArc == null || CanvasChromeSpinner == null) return;

        double screenH = ActualHeight > 120 ? ActualHeight : 767.0;
        double spinDiam = screenH * CHROME_SPINNER_DIAM_H_FRAC;
        double stroke = screenH * CHROME_SPINNER_STROKE_H_FRAC;
        double r = Math.Max(4.0, (spinDiam - stroke) * 0.5);
        double cx = spinDiam * 0.5;
        double cy = spinDiam * 0.5;

        double t = (sharedElapsedOverride ?? GetSharedSessionElapsedSeconds()) * SPEED;
        double phase = (t % CHROME_SPINNER_CYCLE_S) / CHROME_SPINNER_CYCLE_S; // 0..1

        // Base clockwise rotation + Material expand/collapse sweep (45° .. 270°)
        double sweepWave = 0.5 * (1.0 - Math.Cos(phase * 2.0 * Math.PI));
        double sweepDeg = 55.0 + 215.0 * sweepWave;
        double startDeg = (phase * 360.0) - 10.0;
        double endDeg = startDeg + sweepDeg;

        double startRad = startDeg * (Math.PI / 180.0);
        double endRad = endDeg * (Math.PI / 180.0);

        double x0 = cx + r * Math.Sin(startRad);
        double y0 = cy - r * Math.Cos(startRad);
        double x1 = cx + r * Math.Sin(endRad);
        double y1 = cy - r * Math.Cos(endRad);

        var fig = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(x0, y0),
            IsClosed = false,
            IsFilled = false
        };
        fig.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(x1, y1),
            Size = new Windows.Foundation.Size(r, r),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepDeg > 180.0
        });

        var geom = new PathGeometry();
        geom.Figures.Add(fig);
        PathChromeSpinnerArc.Data = geom;
    }

    private static void OnSharedTelemetryTick(object? sender, object e)
    {
        if (SharedActiveControls.Count == 0) return;
        PollSharedGlobalKeys();

        foreach (var ctrl in SharedActiveControls.ToArray())
        {
            ctrl.TickInstanceTelemetry();
        }
    }

    private void TickInstanceTelemetry()
    {
        _telemetryTickCounter++;

        if (_remainingDelayTicks > 0)
        {
            _remainingDelayTicks--;
            int secsLeft = (int)Math.Ceiling(_remainingDelayTicks / 4.0);
            if (_remainingDelayTicks <= 0)
            {
                OverlayStartDelay.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtStartDelayCountdown.Text = $"Starting in {secsLeft}";
                return;
            }
        }

        if (_activeMode != SimulationModeType.CyberOpsTerminal)
        {
            UpdateOsProgressState();
        }

        if (_sharedIsBsodActive)
        {
            UpdateBsodCrashRebootSequence();
        }
    }
}
