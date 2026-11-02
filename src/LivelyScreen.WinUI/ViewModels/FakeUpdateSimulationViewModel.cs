using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LivelyScreen.Models;

namespace LivelyScreen_WinUI.ViewModels;

/// <summary>
/// Clean MVVM ViewModel driving the Windows 10 & Windows 11 Fake Update Screens
/// and the authentic Windows 10/11 Blue Screen of Death (BSOD).
/// 
/// QUICK CUSTOMIZATION GUIDE:
/// - Change Spinner Speed:           See Section 1 (DefaultSpinnerSpeedSeconds / SpinnerSpeedSeconds)
/// - Change Percentage Speed:        See Section 2 (DefaultDurationMinutes / ComputeRealisticPercentage)
/// - Change Update Screen Text:      See Section 3 (Win11Default* / Win10Default* / CustomUpdate*)
/// - Change BSOD Stop Code & Text:   See Section 4 (DefaultBsodStopCode / BsodMainMessage / BsodSecondaryMessage)
/// </summary>
public class FakeUpdateSimulationViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // =========================================================================
    // SECTION 1: [CUSTOMIZE SPINNER SPEED]
    // Change DefaultSpinnerSpeedSeconds (1.0 - 1.3s recommended for smooth arc)
    // =========================================================================
    public const double DefaultSpinnerSpeedSeconds = 1.75;

    private double _spinnerSpeedSeconds = DefaultSpinnerSpeedSeconds;
    public double SpinnerSpeedSeconds
    {
        get => _spinnerSpeedSeconds;
        set => SetField(ref _spinnerSpeedSeconds, Math.Clamp(value, 0.5, 4.0));
    }

    // =========================================================================
    // SECTION 2: [CUSTOMIZE PERCENTAGE INCREASE SPEED & STARTING PERCENTAGE]
    // Change DefaultStartPercent (0-99) and DefaultDurationMinutes (1-480)
    // =========================================================================
    public const int DefaultStartPercent = 7;
    public const int DefaultDurationMinutes = 15;

    private int _startPercent = DefaultStartPercent;
    public int StartPercent
    {
        get => _startPercent;
        set => SetField(ref _startPercent, Math.Clamp(value, 0, 99));
    }

    private int _durationMinutes = DefaultDurationMinutes;
    public int DurationMinutes
    {
        get => _durationMinutes;
        set => SetField(ref _durationMinutes, Math.Clamp(value, 1, 480));
    }

    private int _currentPercent = DefaultStartPercent;
    public int CurrentPercent
    {
        get => _currentPercent;
        set
        {
            if (SetField(ref _currentPercent, Math.Clamp(value, 0, 100)))
            {
                RefreshFormattedUpdateTexts();
            }
        }
    }

    // =========================================================================
    // SECTION 3: [CUSTOMIZE UPDATE SCREEN TEXT (WINDOWS 11 & WINDOWS 10)]
    // Edit the default constants below or pass custom overrides via AppSettings.
    // Use "{0}" or "{X}" in Line 1 to insert the live percentage number.
    // =========================================================================

    // --- Official Windows 11 Update Screen Text ---
    public const string Win11DefaultLine1 = "Working on updates {0}%";
    public const string Win11DefaultLine2 = "Don't turn off your PC. This will take a while.";
    public const string Win11DefaultBottomText = "Your PC may restart several times";

    // --- Official Windows 10 Update Screen Text ---
    public const string Win10DefaultLine1 = "Working on updates {0}%";
    public const string Win10DefaultLine2 = "Don't turn off your PC. This will take a while.";
    public const string Win10DefaultBottomText = "Your PC may restart several times";

    private SimulationModeType _activeMode = SimulationModeType.Windows11Update;
    public SimulationModeType ActiveMode
    {
        get => _activeMode;
        set
        {
            if (SetField(ref _activeMode, value))
            {
                RefreshFormattedUpdateTexts();
            }
        }
    }

    private string _customUpdateLine1 = "";
    public string CustomUpdateLine1
    {
        get => _customUpdateLine1;
        set
        {
            if (SetField(ref _customUpdateLine1, value ?? ""))
            {
                RefreshFormattedUpdateTexts();
            }
        }
    }

    private string _customUpdateLine2 = "";
    public string CustomUpdateLine2
    {
        get => _customUpdateLine2;
        set
        {
            if (SetField(ref _customUpdateLine2, value ?? ""))
            {
                RefreshFormattedUpdateTexts();
            }
        }
    }

    private string _customUpdateBottomText = "";
    public string CustomUpdateBottomText
    {
        get => _customUpdateBottomText;
        set
        {
            if (SetField(ref _customUpdateBottomText, value ?? ""))
            {
                RefreshFormattedUpdateTexts();
            }
        }
    }

    private string _updatePrimaryText = "You are 7% there.";
    public string UpdatePrimaryText
    {
        get => _updatePrimaryText;
        private set => SetField(ref _updatePrimaryText, value);
    }

    private string _updateSecondaryText = Win11DefaultLine2;
    public string UpdateSecondaryText
    {
        get => _updateSecondaryText;
        private set => SetField(ref _updateSecondaryText, value);
    }

    private string _updateBottomRestartText = Win11DefaultBottomText;
    public string UpdateBottomRestartText
    {
        get => _updateBottomRestartText;
        private set => SetField(ref _updateBottomRestartText, value);
    }

    // =========================================================================
    // SECTION 4: [CUSTOMIZE BSOD STOP CODE & MESSAGES]
    // Change DefaultBsodStopCode or any of the BSOD messages below.
    // =========================================================================
    public const string DefaultBsodStopCode = "CRITICAL_PROCESS_DIED";
    public const string DefaultBsodFailedDriver = "ntoskrnl.exe";
    public const string DefaultBsodMainMessage = "Your device ran into a problem and needs to restart.";
    public const string DefaultBsodWin10Line1 = "Your PC ran into a problem and needs to restart. We're";
    public const string DefaultBsodWin10Line2 = "just collecting some error info, and then we'll restart for you.";
    public const string DefaultBsodSecondaryMessage = "We're just collecting some error info, and then we'll restart for you.";
    public const string DefaultBsodSupportUrl = "https://www.windows.com/stopcode";

    public static readonly string[] CommonBsodStopCodes =
    [
        "CRITICAL_PROCESS_DIED",
        "SYSTEM_SERVICE_EXCEPTION",
        "IRQL_NOT_LESS_OR_EQUAL",
        "MEMORY_MANAGEMENT",
        "PAGE_FAULT_IN_NONPAGED_AREA",
        "KERNEL_SECURITY_CHECK_FAILURE",
        "VIDEO_TDR_FAILURE",
        "DPC_watchdog_VIOLATION"
    ];

    private string _bsodStopCode = DefaultBsodStopCode;
    public string BsodStopCode
    {
        get => _bsodStopCode;
        set
        {
            string cleaned = string.IsNullOrWhiteSpace(value) ? DefaultBsodStopCode : value.Trim();
            if (SetField(ref _bsodStopCode, cleaned))
            {
                OnPropertyChanged(nameof(BsodStopCodeLine));
            }
        }
    }

    public string BsodStopCodeLine => $"Stop code: {_bsodStopCode}";

    private string _bsodFailedDriver = DefaultBsodFailedDriver;
    public string BsodFailedDriver
    {
        get => _bsodFailedDriver;
        set
        {
            string cleaned = string.IsNullOrWhiteSpace(value) ? DefaultBsodFailedDriver : value.Trim();
            if (SetField(ref _bsodFailedDriver, cleaned))
            {
                OnPropertyChanged(nameof(BsodWhatFailedLine));
            }
        }
    }

    public string BsodWhatFailedLine => $"What failed: {_bsodFailedDriver}";

    private string _bsodMainMessage = DefaultBsodMainMessage;
    public string BsodMainMessage
    {
        get => _bsodMainMessage;
        set => SetField(ref _bsodMainMessage, string.IsNullOrWhiteSpace(value) ? DefaultBsodMainMessage : value.Trim());
    }

    private string _bsodSecondaryMessage = DefaultBsodSecondaryMessage;
    public string BsodSecondaryMessage
    {
        get => _bsodSecondaryMessage;
        set => SetField(ref _bsodSecondaryMessage, string.IsNullOrWhiteSpace(value) ? DefaultBsodSecondaryMessage : value.Trim());
    }

    private int _bsodPercent = 0;
    public int BsodPercent
    {
        get => _bsodPercent;
        set
        {
            if (SetField(ref _bsodPercent, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(BsodPercentText));
            }
        }
    }

    public string BsodPercentText => $"{_bsodPercent}% complete";

    /// <summary>
    /// Loads all user-configured simulation & BSOD settings from AppSettings.
    /// </summary>
    public void ApplySettings(AppSettings settings, SimulationModeType effectiveMode)
    {
        ActiveMode = effectiveMode;
        SpinnerSpeedSeconds = settings.SimulationSpinnerSpeedSeconds > 0
            ? settings.SimulationSpinnerSpeedSeconds
            : DefaultSpinnerSpeedSeconds;
        StartPercent = settings.SimulationStartPercent;
        DurationMinutes = settings.SimulationDurationMinutes;

        CustomUpdateLine1 = settings.SimulationCustomUpdateLine1;
        CustomUpdateLine2 = settings.SimulationCustomUpdateLine2;
        CustomUpdateBottomText = settings.SimulationCustomUpdateBottomText;

        BsodStopCode = settings.SimulationBsodStopCode;
        BsodFailedDriver = settings.SimulationBsodFailedDriver;
        BsodMainMessage = settings.SimulationBsodMainMessage;
        BsodSecondaryMessage = settings.SimulationBsodSecondaryMessage;

        CurrentPercent = StartPercent;
        RefreshFormattedUpdateTexts();
    }

    /// <summary>
    /// Computes a realistic Windows Update percentage with subtle organic stalls
    /// and bursts while still completing at the configured duration.
    /// Uses a single shared random seed so all mirrored monitors show the exact same percentage.
    /// </summary>
    public int ComputeRealisticPercentage(double linearFraction, int sharedSeed = 0)
    {
        linearFraction = Math.Clamp(linearFraction, 0.0, 1.0);
        if (linearFraction >= 1.0)
            return 100;

        // Deterministic phase offset from shared random seed so all mirrored monitors match 1:1
        double seedPhase = ((sharedSeed & 0xFFFF) / 65536.0) * Math.PI * 2.0;
        double organicWave = (Math.Sin(linearFraction * Math.PI * 6.0 + seedPhase) * 0.016)
                           + (Math.Sin(linearFraction * Math.PI * 14.0 + seedPhase * 1.7) * 0.008);
        double adjusted = Math.Clamp(linearFraction + organicWave * (1.0 - linearFraction), 0.0, 0.999);

        int pct = _startPercent + (int)Math.Floor(adjusted * (100 - _startPercent));
        return Math.Clamp(pct, _startPercent, 99);
    }

    public void RefreshFormattedUpdateTexts()
    {
        bool isWin11 = _activeMode == SimulationModeType.Windows11Update;

        string line1Template = !string.IsNullOrWhiteSpace(_customUpdateLine1)
            ? _customUpdateLine1.Replace("{X}", "{0}", StringComparison.OrdinalIgnoreCase).Replace("%X%", "{0}", StringComparison.OrdinalIgnoreCase)
            : (isWin11 ? Win11DefaultLine1 : Win10DefaultLine1);

        if (line1Template.Contains("{0}"))
        {
            try
            {
                UpdatePrimaryText = string.Format(line1Template, _currentPercent);
            }
            catch
            {
                UpdatePrimaryText = $"{line1Template} {_currentPercent}%";
            }
        }
        else
        {
            UpdatePrimaryText = $"{line1Template} ({_currentPercent}%)";
        }

        UpdateSecondaryText = !string.IsNullOrWhiteSpace(_customUpdateLine2)
            ? _customUpdateLine2
            : (isWin11 ? Win11DefaultLine2 : Win10DefaultLine2);

        UpdateBottomRestartText = !string.IsNullOrWhiteSpace(_customUpdateBottomText)
            ? _customUpdateBottomText
            : (isWin11 ? Win11DefaultBottomText : Win10DefaultBottomText);
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
