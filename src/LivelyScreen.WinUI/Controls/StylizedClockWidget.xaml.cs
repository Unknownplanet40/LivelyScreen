using System;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LivelyScreen.Models;
using Windows.UI;

namespace LivelyScreen_WinUI.Controls;

public sealed partial class StylizedClockWidget : UserControl
{
    private readonly DispatcherTimer _timer;
    private string _lastTimeString = string.Empty;

    #region Dependency Properties

    public static readonly DependencyProperty Use24HourFormatProperty =
        DependencyProperty.Register(
            nameof(Use24HourFormat),
            typeof(bool),
            typeof(StylizedClockWidget),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty ShowSecondsProperty =
        DependencyProperty.Register(
            nameof(ShowSeconds),
            typeof(bool),
            typeof(StylizedClockWidget),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty ShowAmPmProperty =
        DependencyProperty.Register(
            nameof(ShowAmPm),
            typeof(bool),
            typeof(StylizedClockWidget),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty ShowDateProperty =
        DependencyProperty.Register(
            nameof(ShowDate),
            typeof(bool),
            typeof(StylizedClockWidget),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    public static readonly DependencyProperty DatePositionProperty =
        DependencyProperty.Register(
            nameof(DatePosition),
            typeof(ClockDatePosition),
            typeof(StylizedClockWidget),
            new PropertyMetadata(ClockDatePosition.BelowClock, OnVisualPropertyChanged));

    public static readonly DependencyProperty DateFormatProperty =
        DependencyProperty.Register(
            nameof(DateFormat),
            typeof(ClockDateFormat),
            typeof(StylizedClockWidget),
            new PropertyMetadata(ClockDateFormat.SystemLong, OnVisualPropertyChanged));

    public static readonly DependencyProperty ClockFontFamilyProperty =
        DependencyProperty.Register(
            nameof(ClockFontFamily),
            typeof(string),
            typeof(StylizedClockWidget),
            new PropertyMetadata("Segoe UI Variable Display", OnVisualPropertyChanged));

    public static readonly DependencyProperty ClockColorPresetProperty =
        DependencyProperty.Register(
            nameof(ClockColorPreset),
            typeof(string),
            typeof(StylizedClockWidget),
            new PropertyMetadata("Crisp White", OnVisualPropertyChanged));

    public static readonly DependencyProperty ShowGlassPillProperty =
        DependencyProperty.Register(
            nameof(ShowGlassPill),
            typeof(bool),
            typeof(StylizedClockWidget),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty GlassPillOpacityProperty =
        DependencyProperty.Register(
            nameof(GlassPillOpacity),
            typeof(double),
            typeof(StylizedClockWidget),
            new PropertyMetadata(0.40, OnVisualPropertyChanged));

    public static readonly DependencyProperty ClockScaleProperty =
        DependencyProperty.Register(
            nameof(ClockScale),
            typeof(double),
            typeof(StylizedClockWidget),
            new PropertyMetadata(1.0, OnVisualPropertyChanged));

    public bool Use24HourFormat
    {
        get => (bool)GetValue(Use24HourFormatProperty);
        set => SetValue(Use24HourFormatProperty, value);
    }

    public bool ShowSeconds
    {
        get => (bool)GetValue(ShowSecondsProperty);
        set => SetValue(ShowSecondsProperty, value);
    }

    public bool ShowAmPm
    {
        get => (bool)GetValue(ShowAmPmProperty);
        set => SetValue(ShowAmPmProperty, value);
    }

    public bool ShowDate
    {
        get => (bool)GetValue(ShowDateProperty);
        set => SetValue(ShowDateProperty, value);
    }

    public ClockDatePosition DatePosition
    {
        get => (ClockDatePosition)GetValue(DatePositionProperty);
        set => SetValue(DatePositionProperty, value);
    }

    public ClockDateFormat DateFormat
    {
        get => (ClockDateFormat)GetValue(DateFormatProperty);
        set => SetValue(DateFormatProperty, value);
    }

    public string ClockFontFamily
    {
        get => (string)GetValue(ClockFontFamilyProperty);
        set => SetValue(ClockFontFamilyProperty, value);
    }

    public string ClockColorPreset
    {
        get => (string)GetValue(ClockColorPresetProperty);
        set => SetValue(ClockColorPresetProperty, value);
    }

    public bool ShowGlassPill
    {
        get => (bool)GetValue(ShowGlassPillProperty);
        set => SetValue(ShowGlassPillProperty, value);
    }

    public double GlassPillOpacity
    {
        get => (double)GetValue(GlassPillOpacityProperty);
        set => SetValue(GlassPillOpacityProperty, value);
    }

    public double ClockScale
    {
        get => (double)GetValue(ClockScaleProperty);
        set => SetValue(ClockScaleProperty, value);
    }

    #endregion

    public StylizedClockWidget()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateClockDisplay();

        Loaded += (_, _) =>
        {
            ApplyStyleAndColors();
            UpdateVisualScale();
            UpdateClockDisplay(force: true);
            FadeInStoryboard?.Begin();
            _timer.Start();
        };

        Unloaded += (_, _) =>
        {
            _timer.Stop();
        };
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedClockWidget widget)
        {
            widget.ApplyStyleAndColors();
            widget.UpdateVisualScale();
            widget.UpdateClockDisplay(force: true);
        }
    }

    private void ApplyStyleAndColors()
    {
        if (TxtTime == null) return;

        // 1. Resolve Font
        FontFamily resolvedFont = ResolveFont(ClockFontFamily);
        TxtTime.FontFamily = resolvedFont;
        TxtTimeGlow.FontFamily = resolvedFont;
        TxtAmPm.FontFamily = resolvedFont;
        TxtAmPmGlow.FontFamily = resolvedFont;
        TxtDateAbove.FontFamily = resolvedFont;
        TxtDateAboveGlow.FontFamily = resolvedFont;
        TxtDateBelow.FontFamily = resolvedFont;
        TxtDateBelowGlow.FontFamily = resolvedFont;

        // 2. Resolve Color Tint
        Color tintColor = ResolveColor(ClockColorPreset);
        TxtTime.Foreground = new SolidColorBrush(tintColor);
        TxtTimeGlow.Foreground = new SolidColorBrush(Color.FromArgb(40, tintColor.R, tintColor.G, tintColor.B));
        TxtAmPm.Foreground = new SolidColorBrush(Color.FromArgb(235, tintColor.R, tintColor.G, tintColor.B));
        TxtAmPmGlow.Foreground = new SolidColorBrush(Color.FromArgb(32, tintColor.R, tintColor.G, tintColor.B));
        TxtDateAbove.Foreground = new SolidColorBrush(Color.FromArgb(235, tintColor.R, tintColor.G, tintColor.B));
        TxtDateAboveGlow.Foreground = new SolidColorBrush(Color.FromArgb(32, tintColor.R, tintColor.G, tintColor.B));
        TxtDateBelow.Foreground = new SolidColorBrush(Color.FromArgb(235, tintColor.R, tintColor.G, tintColor.B));
        TxtDateBelowGlow.Foreground = new SolidColorBrush(Color.FromArgb(32, tintColor.R, tintColor.G, tintColor.B));

        // 3. Glass Pill Backdrop
        if (PillBackdrop != null)
        {
            if (ShowGlassPill)
            {
                byte alpha = (byte)Math.Clamp((int)(GlassPillOpacity * 255), 10, 240);
                PillBackdrop.Background = new SolidColorBrush(Color.FromArgb(alpha, 18, 22, 32));
                PillBackdrop.BorderThickness = new Thickness(1);
                PillBackdrop.BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255));
                PillBackdrop.CornerRadius = new CornerRadius(28 * Math.Max(0.5, ClockScale));
            }
            else
            {
                PillBackdrop.Background = new SolidColorBrush(Colors.Transparent);
                PillBackdrop.BorderThickness = new Thickness(0);
                PillBackdrop.CornerRadius = new CornerRadius(0);
            }
        }
    }

    private FontFamily ResolveFont(string? fontName)
    {
        return fontName switch
        {
            "SF Pro Display" => new FontFamily("ms-appx:///Assets/Fonts/SFPRODISPLAYBOLD.OTF#SF Pro Display"),
            "Nunito" => new FontFamily("ms-appx:///Assets/Fonts/Nunito.ttf#Nunito"),
            "Bahnschrift" => new FontFamily("Bahnschrift"),
            "Segoe UI" => new FontFamily("Segoe UI"),
            _ => new FontFamily("Segoe UI Variable Display, Segoe UI")
        };
    }

    private Color ResolveColor(string? presetName)
    {
        return presetName switch
        {
            "Ice Blue" => Color.FromArgb(255, 214, 240, 255),
            "Soft Amber" => Color.FromArgb(255, 255, 232, 194),
            "Mint" => Color.FromArgb(255, 216, 245, 229),
            "Lavender" => Color.FromArgb(255, 240, 230, 255),
            _ => Colors.White // Crisp White
        };
    }

    private void UpdateVisualScale()
    {
        if (TxtTime == null) return;

        double scale = Math.Max(0.1, ClockScale);

        // Windows 11 Lock Screen proportions
        double timeFontSize = Math.Max(18, 94 * scale);
        double dateFontSize = Math.Max(9, 22 * scale);
        double amPmFontSize = Math.Max(9, 22 * scale);

        TxtTime.FontSize = timeFontSize;
        TxtTimeGlow.FontSize = timeFontSize;

        TxtAmPm.FontSize = amPmFontSize;
        TxtAmPmGlow.FontSize = amPmFontSize;

        TxtDateAbove.FontSize = dateFontSize;
        TxtDateAboveGlow.FontSize = dateFontSize;

        TxtDateBelow.FontSize = dateFontSize;
        TxtDateBelowGlow.FontSize = dateFontSize;

        // Snug Windows 11 vertical spacing: negative margin offsets internal font line height padding
        PanelDateAbove.Margin = new Thickness(0, 0, 0, -8 * scale);
        PanelDateBelow.Margin = new Thickness(0, -16 * scale, 0, 0);

        if (ShowGlassPill && PillBackdrop != null)
        {
            PillBackdrop.Padding = new Thickness(48 * scale, 16 * scale, 48 * scale, 22 * scale);
        }
        else if (PillBackdrop != null)
        {
            PillBackdrop.Padding = new Thickness(16 * scale, 4 * scale, 16 * scale, 6 * scale);
        }
    }

    private void UpdateClockDisplay(bool force = false)
    {
        if (TxtTime == null) return;

        var now = DateTime.Now;

        // Windows 11 lock screen time formatting (no leading zero for 12h, e.g. "9:41")
        string timeFormat = Use24HourFormat
            ? (ShowSeconds ? "HH:mm:ss" : "HH:mm")
            : (ShowSeconds ? "h:mm:ss" : "h:mm");

        string timeString = now.ToString(timeFormat, CultureInfo.CurrentCulture);

        if (timeString != _lastTimeString)
        {
            TxtTime.Text = timeString;
            TxtTimeGlow.Text = timeString;

            if (!force && TickStoryboard != null)
            {
                TickStoryboard.Begin();
            }

            _lastTimeString = timeString;
        }

        // AM/PM handling
        if (!Use24HourFormat && ShowAmPm)
        {
            string amPm = now.ToString("tt", CultureInfo.CurrentCulture).ToUpperInvariant();
            TxtAmPm.Text = amPm;
            TxtAmPmGlow.Text = amPm;
            PanelAmPm.Visibility = Visibility.Visible;
        }
        else
        {
            PanelAmPm.Visibility = Visibility.Collapsed;
        }

        // Date formatting based on ClockDateFormat
        string dateString = DateFormat switch
        {
            ClockDateFormat.SystemShort => now.ToString("ddd, MMM d", CultureInfo.CurrentCulture),
            ClockDateFormat.Numeric => now.ToString("d", CultureInfo.CurrentCulture),
            _ => now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture) // SystemLong
        };

        TxtDateAbove.Text = dateString;
        TxtDateAboveGlow.Text = dateString;
        TxtDateBelow.Text = dateString;
        TxtDateBelowGlow.Text = dateString;

        bool showAbove = ShowDate && DatePosition == ClockDatePosition.AboveClock;
        bool showBelow = ShowDate && DatePosition == ClockDatePosition.BelowClock;

        PanelDateAbove.Visibility = showAbove ? Visibility.Visible : Visibility.Collapsed;
        PanelDateBelow.Visibility = showBelow ? Visibility.Visible : Visibility.Collapsed;
    }
}
