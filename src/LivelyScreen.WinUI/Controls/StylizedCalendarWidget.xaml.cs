using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LivelyScreen.Models;
using LivelyScreen.Services;

namespace LivelyScreen_WinUI.Controls;

public sealed partial class StylizedCalendarWidget : UserControl
{
    private readonly DispatcherTimer _clockTimer = new();

    public static readonly DependencyProperty CalendarScaleProperty =
        DependencyProperty.Register(nameof(CalendarScale), typeof(double), typeof(StylizedCalendarWidget), new PropertyMetadata(1.0, OnScaleChanged));

    public static readonly DependencyProperty UseSystemAccentProperty =
        DependencyProperty.Register(nameof(UseSystemAccent), typeof(bool), typeof(StylizedCalendarWidget), new PropertyMetadata(false, OnAccentChanged));

    public static readonly DependencyProperty ShowWeekNumberProperty =
        DependencyProperty.Register(nameof(ShowWeekNumber), typeof(bool), typeof(StylizedCalendarWidget), new PropertyMetadata(true, OnCalendarConfigChanged));

    public static readonly DependencyProperty ShowEventNoteProperty =
        DependencyProperty.Register(nameof(ShowEventNote), typeof(bool), typeof(StylizedCalendarWidget), new PropertyMetadata(true, OnCalendarConfigChanged));

    public static readonly DependencyProperty CustomNoteProperty =
        DependencyProperty.Register(nameof(CustomNote), typeof(string), typeof(StylizedCalendarWidget), new PropertyMetadata("No upcoming calendar conflicts", OnCalendarConfigChanged));

    public static readonly DependencyProperty DateFormatStyleProperty =
        DependencyProperty.Register(nameof(DateFormatStyle), typeof(int), typeof(StylizedCalendarWidget), new PropertyMetadata(0, OnCalendarConfigChanged));

    public static readonly DependencyProperty UseLiveEventsProperty =
        DependencyProperty.Register(nameof(UseLiveEvents), typeof(bool), typeof(StylizedCalendarWidget), new PropertyMetadata(true, OnCalendarConfigChanged));

    public static readonly DependencyProperty IcsUrlOrPathProperty =
        DependencyProperty.Register(nameof(IcsUrlOrPath), typeof(string), typeof(StylizedCalendarWidget), new PropertyMetadata("", OnCalendarConfigChanged));

    public double CalendarScale
    {
        get => (double)GetValue(CalendarScaleProperty);
        set => SetValue(CalendarScaleProperty, value);
    }

    public bool UseSystemAccent
    {
        get => (bool)GetValue(UseSystemAccentProperty);
        set => SetValue(UseSystemAccentProperty, value);
    }

    public bool ShowWeekNumber
    {
        get => (bool)GetValue(ShowWeekNumberProperty);
        set => SetValue(ShowWeekNumberProperty, value);
    }

    public bool ShowEventNote
    {
        get => (bool)GetValue(ShowEventNoteProperty);
        set => SetValue(ShowEventNoteProperty, value);
    }

    public string CustomNote
    {
        get => (string)GetValue(CustomNoteProperty);
        set => SetValue(CustomNoteProperty, value);
    }

    public int DateFormatStyle
    {
        get => (int)GetValue(DateFormatStyleProperty);
        set => SetValue(DateFormatStyleProperty, value);
    }

    public bool UseLiveEvents
    {
        get => (bool)GetValue(UseLiveEventsProperty);
        set => SetValue(UseLiveEventsProperty, value);
    }

    public string IcsUrlOrPath
    {
        get => (string)GetValue(IcsUrlOrPathProperty);
        set => SetValue(IcsUrlOrPathProperty, value);
    }

    public StylizedCalendarWidget()
    {
        this.InitializeComponent();
        ApplyScale(CalendarScale);

        _clockTimer.Interval = TimeSpan.FromMinutes(1);
        _clockTimer.Tick += (st, et) =>
        {
            if (Visibility == Visibility.Visible)
                UpdateCalendar();
        };

        this.Loaded += (s, e) =>
        {
            UpdateCalendar();
            ApplyAccentPalette();
            _clockTimer.Start();
        };

        this.Unloaded += (s, e) => _clockTimer.Stop();
    }

    private static void OnCalendarConfigChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedCalendarWidget w)
        {
            w.UpdateCalendar();
        }
    }

    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedCalendarWidget w && e.NewValue is double s)
        {
            w.ApplyScale(s);
        }
    }

    private static void OnAccentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedCalendarWidget w)
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

    private void ApplyAccentPalette()
    {
        if (TxtDayOfMonth == null || BorderDateBadge == null) return;

        if (UseSystemAccent && Application.Current.Resources.TryGetValue("AccentTextFillColorPrimaryBrush", out var accentObj) && accentObj is Brush accentBrush)
        {
            TxtDayOfMonth.Foreground = accentBrush;
            BorderDateBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 255, 255, 255));
        }
        else
        {
            TxtDayOfMonth.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 205, 255));
            BorderDateBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(34, 96, 205, 255));
        }
    }

    private async void UpdateCalendar()
    {
        var now = DateTime.Now;

        if (TxtDayOfWeek != null)
        {
            TxtDayOfWeek.Text = DateFormatStyle switch
            {
                1 => now.ToString("ddd, MMM d"),
                2 => now.ToString("dddd"),
                _ => now.ToString("dddd, MMM d")
            };
        }

        if (TxtDayOfMonth != null) TxtDayOfMonth.Text = now.ToString("dd");
        if (TxtMonthYear != null) TxtMonthYear.Text = now.ToString("MMMM yyyy");

        if (TxtDayProgress != null)
        {
            TxtDayProgress.Visibility = ShowWeekNumber ? Visibility.Visible : Visibility.Collapsed;
            TxtDayProgress.Text = $"Week {System.Globalization.ISOWeek.GetWeekOfYear(now)}";
        }

        if (!ShowEventNote)
        {
            if (TxtAgendaStatus != null) TxtAgendaStatus.Visibility = Visibility.Collapsed;
            if (TxtCalendarFooter != null) TxtCalendarFooter.Visibility = Visibility.Collapsed;
            return;
        }

        if (TxtAgendaStatus != null) TxtAgendaStatus.Visibility = Visibility.Visible;
        if (TxtCalendarFooter != null) TxtCalendarFooter.Visibility = Visibility.Visible;

        if (UseLiveEvents)
        {
            var nextEvt = await LivelyScreen.Services.CalendarSyncService.GetNextUpcomingEventAsync(true, IcsUrlOrPath);
            if (nextEvt != null)
            {
                if (TxtAgendaStatus != null) TxtAgendaStatus.Text = nextEvt.Title;
                if (TxtCalendarFooter != null) TxtCalendarFooter.Text = nextEvt.TimeDescription;
                return;
            }
        }

        if (TxtAgendaStatus != null)
        {
            TxtAgendaStatus.Text = string.IsNullOrWhiteSpace(CustomNote) ? "No upcoming calendar conflicts" : CustomNote;
        }
        if (TxtCalendarFooter != null)
        {
            TxtCalendarFooter.Text = "All clear for the rest of today";
        }
    }
}
