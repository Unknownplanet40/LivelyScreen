namespace LivelyScreen.Models;

public enum MonitorMode
{
    Independent,
    Clone,
    Span
}

public enum TransitionType
{
    Crossfade,
    FadeToBlack,
    Slide,
    Cut
}

public enum PlaybackOrder
{
    Sequential,
    Shuffle,
    Alphabetical,
    DateModified
}

public enum MediaFitMode
{
    Fill,      // Crop to fill screen (no black bars)
    Fit,       // Fit entire media inside screen (letterbox)
    Stretch,   // Distort to fill screen
    Center     // 1:1 Center (no scaling)
}

public class SourceProfile
{
    public TransitionType TransitionStyle { get; set; } = TransitionType.Crossfade;
    public double TransitionDurationSeconds { get; set; } = 1.5;
    public double PhotoDisplaySeconds { get; set; } = 8.0;
    public bool KenBurnsEffect { get; set; } = true;
    public MediaFitMode FitMode { get; set; } = MediaFitMode.Fill;
    public PlaybackOrder? Order { get; set; } = null;
}

public enum AppThemeMode
{
    System = 0,
    Dark = 1,
    Light = 2
}

public enum WidgetPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

public enum VisualFilterPreset
{
    None = 0,
    WarmSepia = 1,
    CinematicCool = 2,
    HighContrastMono = 3,
    VintageFilm = 4,
    CyberpunkNeon = 5
}

public enum AmbientSoundscapeMode
{
    None = 0,
    CustomMusicFolder = 1, // Folder of .flac, .mp3, etc. or single audio file
    SoothingRain = 2,
    WarmHearth = 3,
    CustomAudioFile = 1    // Backward-compat alias
}

public enum ClockDateFormat
{
    SystemLong,
    SystemShort,
    Numeric
}

public enum ClockDatePosition
{
    AboveClock,
    BelowClock
}

public enum HotCornerZone
{
    None = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomLeft = 3,
    BottomRight = 4
}

public enum AutoBlackoutSleepMode
{
    SystemSleep = 0,
    DisplaySleep = 1
}

public enum QuoteSourceType
{
    SqliteDatabase = 0,
    LocalJson = 1,
    OnlineUrl = 2
}

public enum SimulationModeType
{
    None = 0,
    CyberOpsTerminal = 1,
    Windows11Update = 2,
    Windows10Update = 3,
    MacOSUpdate = 4,
    UbuntuUpdate = 5,
    ChromeOSUpdate = 6,
    RandomOSUpdate = 7
}

public enum SimulationSpeedMode
{
    Realistic = 0, // ~1% per 25s
    Moderate = 1,  // ~1% per 5s
    Fast = 2,      // ~1% per 1s
    Frozen99 = 3   // Locked at 99%
}

public enum SimulationFinishAction
{
    RebootLoop = 0,        // Simulated reboot with OS logo, then loops from Start %
    HoldAt99 = 1,          // Freezes at 99% forever
    HoldAt100 = 2,         // Holds at 100% complete
    DismissScreensaver = 3 // Automatically exits screensaver at 100%
}

public enum KnowledgeMapStyle
{
    Celestial3D = 0,
    Plane2D = 1
}

public class AppSettings
{
    public AppThemeMode ThemePreference { get; set; } = AppThemeMode.System;
    public int IdleDelayMinutes { get; set; } = 5;
    public bool StartWithWindows { get; set; } = true;
    public bool SmartPauseInFullscreen { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+Alt+S";

    public HotCornerZone TriggerCorner { get; set; } = HotCornerZone.None;
    public HotCornerZone DisableCorner { get; set; } = HotCornerZone.None;
    public bool PauseOnBattery { get; set; } = false;
    public List<string> BlockedProcesses { get; set; } = new();

    public MonitorMode DisplayMode { get; set; } = MonitorMode.Independent;
    public int TargetDisplayIndex { get; set; } = -1; // -1 = All Displays, 0 = Monitor 1, 1 = Monitor 2...
    public bool PowerOffInactiveDisplays { get; set; } = false;
    public List<string> MediaFolders { get; set; } = new();
    public string ActiveSourcePath { get; set; } = "ALL"; // "ALL" or specific folder path
    public Dictionary<int, string> PerMonitorSourcePath { get; set; } = new();
    public int WidgetTargetDisplayIndex { get; set; } = -1; // -1 = Primary Only, -2 = All Monitors, >= 0 = Specific Monitor Index
    public bool ShowWidgetsOnAllMonitors
    {
        get => WidgetTargetDisplayIndex == -2;
        set
        {
            if (value) WidgetTargetDisplayIndex = -2;
            else if (WidgetTargetDisplayIndex == -2) WidgetTargetDisplayIndex = -1;
        }
    }

    public string GetSourceForDisplay(int displayIndex)
    {
        if (PerMonitorSourcePath != null &&
            PerMonitorSourcePath.TryGetValue(displayIndex, out var p) &&
            !string.IsNullOrWhiteSpace(p))
        {
            return p;
        }
        return string.IsNullOrEmpty(ActiveSourcePath) ? "ALL" : ActiveSourcePath;
    }

    public TransitionType TransitionStyle { get; set; } = TransitionType.Crossfade;
    public double TransitionDurationSeconds { get; set; } = 1.5;
    public double PhotoDisplaySeconds { get; set; } = 8.0;
    public bool KenBurnsEffect { get; set; } = true;
    public MediaFitMode FitMode { get; set; } = MediaFitMode.Fill;
    public Dictionary<string, SourceProfile> FolderProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public SourceProfile GetProfileForPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return new SourceProfile { TransitionStyle = TransitionStyle, TransitionDurationSeconds = TransitionDurationSeconds, PhotoDisplaySeconds = PhotoDisplaySeconds, KenBurnsEffect = KenBurnsEffect, FitMode = FitMode, Order = Order };

        if (FolderProfiles.TryGetValue(path, out var exactProfile))
            return exactProfile;

        foreach (var kvp in FolderProfiles)
        {
            if (!string.IsNullOrEmpty(kvp.Key) && path.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return new SourceProfile { TransitionStyle = TransitionStyle, TransitionDurationSeconds = TransitionDurationSeconds, PhotoDisplaySeconds = PhotoDisplaySeconds, KenBurnsEffect = KenBurnsEffect, FitMode = FitMode, Order = Order };
    }

    public MediaFitMode GetFitModeForPath(string? path) => GetProfileForPath(path).FitMode;

    public bool VideoAudioEnabled { get; set; } = false;
    public int VideoVolumePercent { get; set; } = 0;
    public PlaybackOrder Order { get; set; } = PlaybackOrder.Sequential;

    public bool DynamicThemeEnabled { get; set; } = true;
    public DynamicThemeMode ThemeMode { get; set; } = DynamicThemeMode.SolarTimeLocked;
    public string? SelectedThemeId { get; set; } = null;
    public bool UseCustomSolarTimes { get; set; } = false;
    public string CustomSunriseTime { get; set; } = "06:00";
    public string CustomSunsetTime { get; set; } = "18:00";
    public SolarSegmentOverride SegmentOverride { get; set; } = SolarSegmentOverride.AutoRealTime;

    // Fake OS Updates & CyberOps Simulation Suite
    public const string MultiMonitorModePrimaryOnly = "primary_only";
    public const string MultiMonitorModeMirror = "mirror";

    public bool SimulationModeEnabled { get; set; } = false;
    public bool MASK_SENSITIVE { get; set; } = false;
    public bool SimulationMaskSensitive
    {
        get => MASK_SENSITIVE;
        set => MASK_SENSITIVE = value;
    }
    public string MULTI_MONITOR_MODE { get; set; } = MultiMonitorModePrimaryOnly;
    public string SimulationMultiMonitorMode
    {
        get => string.Equals(MULTI_MONITOR_MODE, MultiMonitorModeMirror, StringComparison.OrdinalIgnoreCase)
            ? MultiMonitorModeMirror
            : MultiMonitorModePrimaryOnly;
        set => MULTI_MONITOR_MODE = string.Equals(value, MultiMonitorModeMirror, StringComparison.OrdinalIgnoreCase)
            ? MultiMonitorModeMirror
            : MultiMonitorModePrimaryOnly;
    }
    public SimulationModeType SelectedSimulationMode { get; set; } = SimulationModeType.CyberOpsTerminal;
    public int SimulationStartPercent { get; set; } = 7;
    public int SimulationDurationMinutes { get; set; } = 15;
    public bool SimulationIsCustomDuration { get; set; } = false;
    public int SimulationStartDelaySeconds { get; set; } = 0;
    public SimulationFinishAction SimulationWhenFinished { get; set; } = SimulationFinishAction.RebootLoop;
    public bool SimulationShowLogoOnUpdate { get; set; } = true;
    public SimulationSpeedMode SimulationSpeed { get; set; } = SimulationSpeedMode.Realistic;
    public bool SimulationEnterTriggerBsod { get; set; } = true;
    public double SimulationSpinnerSpeedSeconds { get; set; } = 1.75; // 1.0 - 1.3s per rotation default
    public string SimulationBsodStopCode { get; set; } = "CRITICAL_PROCESS_DIED";
    public string SimulationBsodFailedDriver { get; set; } = "ntoskrnl.exe";
    public string SimulationBsodMainMessage { get; set; } = "Your device ran into a problem and needs to restart.";
    public string SimulationBsodSecondaryMessage { get; set; } = "We're just collecting some error info, and then we'll restart for you.";
    public string SimulationCustomUpdateLine1 { get; set; } = "";
    public string SimulationCustomUpdateLine2 { get; set; } = "";
    public string SimulationCustomUpdateBottomText { get; set; } = "";

    public bool IsSimulationActive =>
        SimulationModeEnabled ||
        (!string.IsNullOrEmpty(ActiveSourcePath) && ActiveSourcePath.StartsWith("SIM:", StringComparison.OrdinalIgnoreCase));

    public SimulationModeType GetEffectiveSimulationMode(string? overrideSourcePath = null)
    {
        if (TryParseSimulationSource(overrideSourcePath, out var overrideMode))
            return overrideMode;
        if (TryParseSimulationSource(ActiveSourcePath, out var activeMode))
            return activeMode;
        if (SimulationModeEnabled && SelectedSimulationMode != SimulationModeType.None)
            return SelectedSimulationMode;
        return SimulationModeType.None;
    }

    public static string GetSimulationSourceKey(SimulationModeType mode) => $"SIM:{mode}";

    public static bool TryParseSimulationSource(string? sourcePath, out SimulationModeType mode)
    {
        mode = SimulationModeType.None;
        if (string.IsNullOrWhiteSpace(sourcePath) || !sourcePath.StartsWith("SIM:", StringComparison.OrdinalIgnoreCase))
            return false;

        var raw = sourcePath.Substring(4).Trim();
        return Enum.TryParse(raw, true, out mode) && mode != SimulationModeType.None;
    }

    public static string GetSimulationDisplayName(SimulationModeType mode) => mode switch
    {
        SimulationModeType.CyberOpsTerminal => "CyberOps SOC Terminal (Hacker Simulator)",
        SimulationModeType.Windows11Update => "Windows 11 System Update",
        SimulationModeType.Windows10Update => "Windows 10 System Update",
        SimulationModeType.MacOSUpdate => "macOS Firmware Update",
        SimulationModeType.UbuntuUpdate => "Ubuntu Linux Boot / Update",
        SimulationModeType.ChromeOSUpdate => "ChromeOS System Update",
        SimulationModeType.RandomOSUpdate => "Random OS Update (Shuffle)",
        _ => "Simulation Mode"
    };

    // Stash Knowledge Map Screensaver
    public const string KnowledgeMapSourceKey = "KNOWLEDGE_MAP";
    public bool KnowledgeMapEnabled { get; set; } = false;
    public string KnowledgeMapDatabasePath { get; set; } = @"C:\Users\ryanj\OneDrive\Desktop\Applications\KnowledgeBase-Quotes\data\brain.db";
    public KnowledgeMapStyle KnowledgeMapStyle { get; set; } = KnowledgeMapStyle.Celestial3D;
    public double KnowledgeMapOrbitSpeed { get; set; } = 1.0; // 0.2x to 3.0x
    public double KnowledgeMapCameraPitch { get; set; } = 28.0; // 0 to 65 degrees
    public double KnowledgeMapDepthSpread { get; set; } = 360.0; // 50 to 600
    public bool KnowledgeMapSuppressWidgets { get; set; } = false;

    public bool IsKnowledgeMapActive(string? overrideSourcePath = null)
    {
        if (!LivelyScreen.Services.KnowledgeMapService.IsAvailable(KnowledgeMapDatabasePath))
            return false;

        if (string.Equals(overrideSourcePath, KnowledgeMapSourceKey, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(ActiveSourcePath, KnowledgeMapSourceKey, StringComparison.OrdinalIgnoreCase))
            return true;
        return KnowledgeMapEnabled;
    }

    public int MouseSensitivityPixels { get; set; } = 25;
    public bool AllowDisplaySleep { get; set; } = true;
    public bool OledPixelShiftEnabled { get; set; } = true;
    public int AutoBlackoutMinutes { get; set; } = 0; // 0 = Disabled, >0 = fade to black & allow sleep after idle minutes
    public bool AutoBlackoutSleepEnabled { get; set; } = false;
    public AutoBlackoutSleepMode AutoBlackoutSleepAction { get; set; } = AutoBlackoutSleepMode.SystemSleep;

    // Clock Overlay
    public bool WidgetsEnabled { get; set; } = true;
    public bool ClockWidgetEnabled { get; set; } = true;
    public WidgetPosition ClockPosition { get; set; } = WidgetPosition.TopCenter;
    public bool ClockUse12Hour { get; set; } = true;
    public bool ClockShowSeconds { get; set; } = false;
    public bool ClockShowAmPm { get; set; } = false;
    public bool ClockShowDate { get; set; } = true;
    public ClockDatePosition DatePosition { get; set; } = ClockDatePosition.BelowClock;
    public ClockDateFormat DateFormat { get; set; } = ClockDateFormat.SystemLong;
    public string ClockFontFamily { get; set; } = "Segoe UI Variable Display";
    public string ClockColorPreset { get; set; } = "Crisp White";
    public bool ShowGlassPill { get; set; } = false;
    public double GlassPillOpacity { get; set; } = 0.40;
    public double ClockScale { get; set; } = 1.0;
    public bool ClockShuffleEnabled { get; set; } = true;
    public int ClockShuffleSeconds { get; set; } = 10;

    // Shared Widget Palette Style (false = Win11 Calm Palette, true = System Accent Sync)
    public bool WidgetUseSystemAccent { get; set; } = false;

    // Battery Status Badge
    public bool BatteryWidgetEnabled { get; set; } = true;

    // Weather Widget
    public bool WeatherWidgetEnabled { get; set; } = false;
    public WidgetPosition WeatherPosition { get; set; } = WidgetPosition.BottomCenter;
    public double WeatherScale { get; set; } = 1.0;
    public string WeatherCity { get; set; } = "Auto";
    public bool WeatherUseFahrenheit { get; set; } = false;

    // Media / Now Playing Widget
    public bool MediaWidgetEnabled { get; set; } = false;
    public WidgetPosition MediaPosition { get; set; } = WidgetPosition.BottomCenter;
    public double MediaScale { get; set; } = 1.0;
    public bool MediaShowAlbumArt { get; set; } = true;
    public bool MediaHideWhenIdle { get; set; } = false;
    public string MediaIdleText { get; set; } = "Ready for Music";

    // Performance Monitor Widget
    public bool PerformanceWidgetEnabled { get; set; } = false;
    public WidgetPosition PerformancePosition { get; set; } = WidgetPosition.BottomCenter;
    public double PerformanceScale { get; set; } = 1.0;
    public bool PerformanceShowCpu { get; set; } = true;
    public bool PerformanceShowRam { get; set; } = true;
    public bool PerformanceShowPercentages { get; set; } = true;
    public int PerformanceHighlightThreshold { get; set; } = 85;
    public int PerformanceRefreshIntervalSeconds { get; set; } = 2;

    // Calendar / Agenda Widget
    public bool CalendarWidgetEnabled { get; set; } = false;
    public WidgetPosition CalendarPosition { get; set; } = WidgetPosition.BottomCenter;
    public double CalendarScale { get; set; } = 1.0;
    public bool CalendarShowWeekNumber { get; set; } = true;
    public bool CalendarShowEventNote { get; set; } = true;
    public string CalendarCustomNote { get; set; } = "No upcoming calendar conflicts";
    public int CalendarDateFormat { get; set; } = 0;
    public bool CalendarUseLiveEvents { get; set; } = true;
    public string CalendarIcsUrlOrPath { get; set; } = "";

    // Wisdom & Quotes Widget (KnowledgeBase SQLite Integration — Independent Literary Overlay)
    public bool QuoteWidgetEnabled { get; set; } = true;
    public WidgetPosition QuotePosition { get; set; } = WidgetPosition.Center;
    public bool QuoteIndependentMigrated { get; set; } = false;
    public double QuoteScale { get; set; } = 1.0;
    public double QuoteCardWidth { get; set; } = 340.0;
    public double QuoteBackgroundOpacity { get; set; } = 0.85;
    public bool QuoteShowBorder { get; set; } = true;
    public bool QuoteBlurEffect { get; set; } = true;
    public double QuoteBlurIntensity { get; set; } = 18.0;
    public string QuoteBackdropTheme { get; set; } = "Acrylic";
    public int QuoteTextAlignment { get; set; } = 0; // 0 = Left Editorial, 1 = Centered Poetry
    public string QuoteFontStyle { get; set; } = "Literary Serif";
    public double QuoteMainFontSize { get; set; } = 16.0;
    public double QuoteMeaningFontSize { get; set; } = 11.5;
    public string QuoteColorTheme { get; set; } = "Warm Cream & Muted Gold";
    public bool QuoteShowCategoryHeader { get; set; } = true;
    public bool QuoteShowAuthor { get; set; } = true;
    public bool QuoteShowMeaning { get; set; } = true;
    public int QuoteRotationIntervalSeconds { get; set; } = 30;
    public QuoteSourceType QuoteSource { get; set; } = QuoteSourceType.SqliteDatabase;
    public string QuoteDatabasePath { get; set; } = "";
    public string QuoteLocalJsonPath { get; set; } = "";
    public string QuoteOnlineUrl { get; set; } = "";
    public DateTime QuoteLastOnlineSync { get; set; } = DateTime.MinValue;
    public string QuotesCacheJson { get; set; } = "";

    // Post-Processing Filters & EXIF Pill
    public int BrightnessDimPercent { get; set; } = 0; // 0 (normal) to 80 (dim)
    public VisualFilterPreset VisualFilter { get; set; } = VisualFilterPreset.None;
    public bool ShowMediaInfoPill { get; set; } = false;

    // Ambient Soundscapes & Audio Spectrum
    public bool AudioSpectrumEnabled { get; set; } = true;
    public AmbientSoundscapeMode AmbientSoundMode { get; set; } = AmbientSoundscapeMode.None;
    public int AmbientSoundVolume { get; set; } = 35; // 0 to 100%
    public string AmbientCustomAudioPath { get; set; } = "";
    public bool AmbientMusicShuffle { get; set; } = true;

    // Independent Per-Monitor Widget Profiles
    public Dictionary<int, MonitorWidgetProfile> PerMonitorWidgets { get; set; } = new();

    public MonitorWidgetProfile GetWidgetProfileForDisplay(int displayIndex)
    {
        if (PerMonitorWidgets != null && PerMonitorWidgets.TryGetValue(displayIndex, out var p) && p != null)
            return p;

        if (displayIndex == 0)
        {
            return new MonitorWidgetProfile
            {
                ClockEnabled = ClockWidgetEnabled,
                ClockPosition = ClockPosition,
                WeatherEnabled = WeatherWidgetEnabled,
                WeatherPosition = WeatherPosition,
                MediaEnabled = MediaWidgetEnabled,
                MediaPosition = MediaPosition,
                PerformanceEnabled = PerformanceWidgetEnabled,
                PerformancePosition = PerformancePosition,
                CalendarEnabled = CalendarWidgetEnabled,
                CalendarPosition = CalendarPosition,
                QuoteEnabled = QuoteWidgetEnabled,
                QuotePosition = QuotePosition
            };
        }

        // Secondary monitors (Monitor 2, 3...) start completely empty by default
        return new MonitorWidgetProfile
        {
            ClockEnabled = false,
            ClockPosition = WidgetPosition.TopCenter,
            WeatherEnabled = false,
            WeatherPosition = WidgetPosition.BottomCenter,
            MediaEnabled = false,
            MediaPosition = WidgetPosition.BottomCenter,
            PerformanceEnabled = false,
            PerformancePosition = WidgetPosition.BottomCenter,
            CalendarEnabled = false,
            CalendarPosition = WidgetPosition.BottomCenter,
            QuoteEnabled = false,
            QuotePosition = WidgetPosition.Center
        };
    }

    public MonitorWidgetProfile GetOrCreateWidgetProfileForDisplay(int displayIndex)
    {
        if (PerMonitorWidgets == null) PerMonitorWidgets = new();
        if (!PerMonitorWidgets.TryGetValue(displayIndex, out var p) || p == null)
        {
            p = GetWidgetProfileForDisplay(displayIndex);
            PerMonitorWidgets[displayIndex] = p;
        }
        return p;
    }
}

public class MonitorWidgetProfile
{
    public bool ClockEnabled { get; set; } = true;
    public WidgetPosition ClockPosition { get; set; } = WidgetPosition.TopCenter;

    public bool WeatherEnabled { get; set; } = false;
    public WidgetPosition WeatherPosition { get; set; } = WidgetPosition.BottomCenter;

    public bool MediaEnabled { get; set; } = false;
    public WidgetPosition MediaPosition { get; set; } = WidgetPosition.BottomCenter;

    public bool PerformanceEnabled { get; set; } = false;
    public WidgetPosition PerformancePosition { get; set; } = WidgetPosition.BottomCenter;

    public bool CalendarEnabled { get; set; } = false;
    public WidgetPosition CalendarPosition { get; set; } = WidgetPosition.BottomCenter;

    public bool QuoteEnabled { get; set; } = false;
    public WidgetPosition QuotePosition { get; set; } = WidgetPosition.Center;
}

