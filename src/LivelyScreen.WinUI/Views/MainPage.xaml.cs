using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;
using Windows.ApplicationModel.DataTransfer;
using LivelyScreen.Models;
using LivelyScreen.Native;
using LivelyScreen.Services;

namespace LivelyScreen_WinUI.Views;

public sealed partial class MainPage : Page
{
    private SettingsService? _settingsService;
    private MediaLibrary? _mediaLibrary;
    private IdleDetector? _idleDetector;
    private ScreensaverManager? _screensaverManager;
    private IntPtr _windowHandle = IntPtr.Zero;
    private bool _isInitializing = true;

    public event Action? ThemePreferenceChanged;
    public event Action? ScreensaverRequested;

    public MainPage()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            if (NavView.SelectedItem == null && NavView.MenuItems.Count > 0)
                NavView.SelectedItem = NavView.MenuItems[0];
            UpdatePaneFooterLayout(NavView.IsPaneOpen);
        };
        NavView.DisplayModeChanged += (s, e) => UpdatePaneFooterLayout(NavView.IsPaneOpen);
    }

    public void InitializeServices(
        Window window,
        SettingsService settingsService,
        MediaLibrary mediaLibrary,
        IdleDetector idleDetector,
        ScreensaverManager screensaverManager)
    {
        _windowHandle = WindowNative.GetWindowHandle(window);
        _settingsService = settingsService;
        _mediaLibrary = mediaLibrary;
        _idleDetector = idleDetector;
        _screensaverManager = screensaverManager;

        _mediaLibrary.LibraryUpdated += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateMediaCountText();
                UpdateSourceList();
            });
        };

        PopulateSettingsToUI();
        _isInitializing = false;
    }

    public void PopulateSettingsToUI()
    {
        if (_settingsService == null) return;
        var s = _settingsService.Current;

        SliderIdleMinutes.Value = s.IdleDelayMinutes;
        TxtIdleMinutes.Text = $"{s.IdleDelayMinutes} min";

        ToggleAutoStart.IsOn = AutoStartService.IsAutoStartEnabled();
        ToggleSmartPause.IsOn = s.SmartPauseInFullscreen;

        ComboDisplayMode.SelectedIndex = (int)s.DisplayMode;
        UpdateScreenInfo();

        UpdateSourceList();
        ComboPlaybackOrder.SelectedIndex = (int)s.Order;
        ComboThemeMode.SelectedIndex = (int)s.ThemeMode;
        ComboSegmentOverride.SelectedIndex = (int)s.SegmentOverride;

        ToggleCustomSolar.IsOn = s.UseCustomSolarTimes;

        if (TimeSpan.TryParse(s.CustomSunriseTime, out var srTime))
            TimePickerSunrise.Time = srTime;
        else
            TimePickerSunrise.Time = new TimeSpan(6, 0, 0);

        if (TimeSpan.TryParse(s.CustomSunsetTime, out var ssTime))
            TimePickerSunset.Time = ssTime;
        else
            TimePickerSunset.Time = new TimeSpan(18, 0, 0);

        UpdateMediaCountText();

        ToggleAudio.IsOn = s.VideoAudioEnabled;
        SliderVolume.Value = s.VideoVolumePercent;
        TxtVolume.Text = $"{s.VideoVolumePercent}%";
        SliderVolume.IsEnabled = s.VideoAudioEnabled;

        if (ComboVisualFilter != null) ComboVisualFilter.SelectedIndex = (int)s.VisualFilter;
        if (SliderBrightnessDim != null)
        {
            SliderBrightnessDim.Value = Math.Clamp(s.BrightnessDimPercent, 0, 80);
            if (TxtBrightnessDim != null) TxtBrightnessDim.Text = $"{SliderBrightnessDim.Value:F0}%";
        }
        if (ToggleMediaInfoPill != null) ToggleMediaInfoPill.IsOn = s.ShowMediaInfoPill;

        if (ComboAmbientSound != null) ComboAmbientSound.SelectedIndex = (int)s.AmbientSoundMode;
        if (SliderAmbientVolume != null)
        {
            SliderAmbientVolume.Value = Math.Clamp(s.AmbientSoundVolume, 0, 100);
            if (TxtAmbientVolume != null) TxtAmbientVolume.Text = $"{SliderAmbientVolume.Value:F0}%";
        }
        if (TxtAmbientCustomPath != null) TxtAmbientCustomPath.Text = s.AmbientCustomAudioPath ?? "";
        if (ToggleAmbientShuffle != null) ToggleAmbientShuffle.IsOn = s.AmbientMusicShuffle;

        SliderSensitivity.Value = s.MouseSensitivityPixels;
        TxtSensitivity.Text = $"{s.MouseSensitivityPixels} px";

        ToggleDisplaySleep.IsOn = s.AllowDisplaySleep;
        ToggleOledShift.IsOn = s.OledPixelShiftEnabled;
        ComboAutoBlackout.SelectedIndex = s.AutoBlackoutMinutes switch
        {
            5 => 1,
            10 => 2,
            15 => 3,
            30 => 4,
            60 => 5,
            _ => 0
        };
        ComboTriggerCorner.SelectedIndex = (int)s.TriggerCorner;
        ComboDisableCorner.SelectedIndex = (int)s.DisableCorner;
        TogglePauseOnBattery.IsOn = s.PauseOnBattery;
        TxtBlockedProcesses.Text = s.BlockedProcesses != null && s.BlockedProcesses.Count > 0
            ? string.Join(", ", s.BlockedProcesses)
            : "";
        ComboAppTheme.SelectedIndex = (int)s.ThemePreference;

        ToggleWidgetsEnabled.IsOn = s.WidgetsEnabled;
        ToggleWidgetsAllMonitors.IsOn = s.ShowWidgetsOnAllMonitors;
        ToggleClockWidget.IsOn = s.ClockWidgetEnabled;
        ComboClockFont.SelectedIndex = s.ClockFontFamily switch
        {
            "SF Pro Display" => 1,
            "Nunito" => 2,
            "Bahnschrift" => 3,
            "Segoe UI" => 4,
            _ => 0
        };
        ComboClockColor.SelectedIndex = s.ClockColorPreset switch
        {
            "Ice Blue" => 1,
            "Soft Amber" => 2,
            "Mint" => 3,
            "Lavender" => 4,
            _ => 0
        };
        ComboClockFormat.SelectedIndex = s.ClockUse12Hour ? 0 : 1;
        ToggleClockAmPm.IsOn = s.ClockShowAmPm;
        ToggleClockSeconds.IsOn = s.ClockShowSeconds;
        ToggleClockDate.IsOn = s.ClockShowDate;
        ComboDateFormat.SelectedIndex = (int)s.DateFormat;
        ComboDatePosition.SelectedIndex = s.DatePosition == ClockDatePosition.AboveClock ? 1 : 0;
        ToggleGlassPill.IsOn = s.ShowGlassPill;
        SliderGlassOpacity.Value = Math.Round(s.GlassPillOpacity * 100);
        TxtGlassOpacity.Text = $"{SliderGlassOpacity.Value:F0}%";
        SliderClockScale.Value = Math.Round(s.ClockScale * 100);
        TxtClockScale.Text = $"{SliderClockScale.Value:F0}%";
        TogglePositionShuffle.IsOn = s.ClockShuffleEnabled;
        ComboShuffleInterval.SelectedIndex = s.ClockShuffleSeconds switch
        {
            30 => 1,
            60 => 2,
            300 => 3,
            _ => 0
        };
        ComboWidgetAccentStyle.SelectedIndex = s.WidgetUseSystemAccent ? 1 : 0;

        // Weather Settings
        ToggleWeatherWidget.IsOn = s.WeatherWidgetEnabled;
        TxtWeatherCity.Text = s.WeatherCity ?? "Auto";
        ComboWeatherUnit.SelectedIndex = s.WeatherUseFahrenheit ? 1 : 0;
        SliderWeatherScale.Value = Math.Round(s.WeatherScale * 100);
        TxtWeatherScale.Text = $"{SliderWeatherScale.Value:F0}%";

        // Media Settings
        ToggleMediaWidget.IsOn = s.MediaWidgetEnabled;
        SliderMediaScale.Value = Math.Round(s.MediaScale * 100);
        TxtMediaScale.Text = $"{SliderMediaScale.Value:F0}%";
        ToggleMediaAlbumArt.IsOn = s.MediaShowAlbumArt;
        ToggleMediaHideWhenIdle.IsOn = s.MediaHideWhenIdle;
        TxtMediaIdleText.Text = s.MediaIdleText ?? "Ready for Music";
        if (ToggleAudioSpectrum != null) ToggleAudioSpectrum.IsOn = s.AudioSpectrumEnabled;

        // Performance Settings
        TogglePerformanceWidget.IsOn = s.PerformanceWidgetEnabled;
        SliderPerformanceScale.Value = Math.Round(s.PerformanceScale * 100);
        TxtPerformanceScale.Text = $"{SliderPerformanceScale.Value:F0}%";
        TogglePerfShowCpu.IsOn = s.PerformanceShowCpu;
        TogglePerfShowRam.IsOn = s.PerformanceShowRam;
        TogglePerfPercentages.IsOn = s.PerformanceShowPercentages;
        ComboPerfThreshold.SelectedIndex = s.PerformanceHighlightThreshold switch
        {
            80 => 0,
            85 => 1,
            90 => 2,
            95 => 3,
            _ => 4
        };
        ComboPerfFrequency.SelectedIndex = s.PerformanceRefreshIntervalSeconds switch
        {
            1 => 0,
            5 => 2,
            _ => 1
        };

        // Calendar Settings
        ToggleCalendarWidget.IsOn = s.CalendarWidgetEnabled;
        SliderCalendarScale.Value = Math.Round(s.CalendarScale * 100);
        TxtCalendarScale.Text = $"{SliderCalendarScale.Value:F0}%";
        ComboCalendarFormat.SelectedIndex = Math.Clamp(s.CalendarDateFormat, 0, 2);
        ToggleCalendarWeekNumber.IsOn = s.CalendarShowWeekNumber;
        ToggleCalendarShowEventNote.IsOn = s.CalendarShowEventNote;
        TxtCalendarCustomNote.Text = s.CalendarCustomNote ?? "No upcoming calendar conflicts";
        if (ToggleCalendarLiveEvents != null) ToggleCalendarLiveEvents.IsOn = s.CalendarUseLiveEvents;
        if (TxtCalendarIcsPath != null) TxtCalendarIcsPath.Text = s.CalendarIcsUrlOrPath ?? "";

        // Wisdom & Quotes Settings
        if (!s.QuoteIndependentMigrated)
        {
            s.QuotePosition = WidgetPosition.Center;
            s.QuoteIndependentMigrated = true;
            _settingsService.Save();
        }
        ToggleQuoteWidget.IsOn = s.QuoteWidgetEnabled;
        TxtQuoteDbPath.Text = s.QuoteDatabasePath ?? string.Empty;

        if (ComboQuoteColorTheme != null)
        {
            ComboQuoteColorTheme.SelectedIndex = (s.QuoteColorTheme ?? "Warm Cream & Muted Gold") switch
            {
                "Crisp White & Silver" => 1,
                "Soft Lavender & Moonlight" => 2,
                "Sage & Parchment" => 3,
                "Windows System Accent" => 4,
                _ => 0
            };
        }

        if (SliderQuoteBgOpacity != null)
        {
            SliderQuoteBgOpacity.Value = Math.Round(s.QuoteBackgroundOpacity * 100);
            if (TxtQuoteBgOpacity != null) TxtQuoteBgOpacity.Text = $"{SliderQuoteBgOpacity.Value:F0}%";
        }
        if (ToggleQuoteShowBorder != null) ToggleQuoteShowBorder.IsOn = s.QuoteShowBorder;
        if (ToggleQuoteBlurEffect != null) ToggleQuoteBlurEffect.IsOn = s.QuoteBlurEffect;

        if (ComboQuoteBackdropTheme != null)
        {
            ComboQuoteBackdropTheme.SelectedIndex = (s.QuoteBackdropTheme ?? "Acrylic") switch
            {
                "Custom Frosted Blur" or "Frosted Blur" => 1,
                "Mica Satin (Subtle Luster)" or "Mica Satin" => 2,
                _ => 0
            };
        }

        if (SliderQuoteBlurIntensity != null)
        {
            SliderQuoteBlurIntensity.Value = Math.Clamp(s.QuoteBlurIntensity, 4, 60);
            if (TxtQuoteBlurIntensity != null) TxtQuoteBlurIntensity.Text = $"{SliderQuoteBlurIntensity.Value:F0} px";
        }

        if (ComboQuoteFontStyle != null)
        {
            ComboQuoteFontStyle.SelectedIndex = (s.QuoteFontStyle ?? "Literary Serif") switch
            {
                "Modern Display Sans" => 1,
                "Classic Book Antiqua" => 2,
                "Monospace" => 3,
                _ => 0
            };
        }

        if (SliderQuoteMainFontSize != null)
        {
            SliderQuoteMainFontSize.Value = Math.Clamp(s.QuoteMainFontSize, 12, 28);
            if (TxtQuoteMainFontSize != null) TxtQuoteMainFontSize.Text = $"{SliderQuoteMainFontSize.Value:F0} pt";
        }

        if (SliderQuoteMeaningFontSize != null)
        {
            SliderQuoteMeaningFontSize.Value = Math.Clamp(s.QuoteMeaningFontSize, 9, 16);
            if (TxtQuoteMeaningFontSize != null) TxtQuoteMeaningFontSize.Text = $"{SliderQuoteMeaningFontSize.Value:0.#} pt";
        }

        if (ComboQuoteAlignment != null)
        {
            ComboQuoteAlignment.SelectedIndex = Math.Clamp(s.QuoteTextAlignment, 0, 1);
        }

        if (SliderQuoteCardWidth != null)
        {
            SliderQuoteCardWidth.Value = Math.Clamp(s.QuoteCardWidth, 260, 520);
            if (TxtQuoteCardWidth != null) TxtQuoteCardWidth.Text = $"{SliderQuoteCardWidth.Value:F0} px";
        }

        SliderQuoteScale.Value = Math.Round(s.QuoteScale * 100);
        TxtQuoteScale.Text = $"{SliderQuoteScale.Value:F0}%";

        if (ToggleQuoteShowCategory != null) ToggleQuoteShowCategory.IsOn = s.QuoteShowCategoryHeader;
        if (ToggleQuoteShowAuthor != null) ToggleQuoteShowAuthor.IsOn = s.QuoteShowAuthor;
        if (ToggleQuoteShowMeaning != null) ToggleQuoteShowMeaning.IsOn = s.QuoteShowMeaning;

        ComboQuoteRotation.SelectedIndex = s.QuoteRotationIntervalSeconds switch
        {
            15 => 0,
            30 => 1,
            60 => 2,
            300 => 3,
            0 => 4,
            _ => 1
        };
        UpdateQuoteDbStatusUI(s.QuoteDatabasePath);

        UpdateAllWidgetPositionPreviews();
        UpdateMiniMonitorWallpaper();

        UpdateControlStates();
    }

    public void UpdateScreenInfo()
    {
        if (_settingsService == null) return;
        try
        {
            var displays = DisplayArea.FindAll();
            var primary = DisplayArea.Primary;
            var w = primary?.OuterBounds.Width ?? 1920;
            var h = primary?.OuterBounds.Height ?? 1080;
            TxtDetectedDisplays.Text = $"{displays.Count} Display(s) Active (Primary: {w}x{h})";

            ComboTargetDisplay.Items.Clear();
            ComboTargetDisplay.Items.Add(new ComboBoxItem { Content = "All Displays", Tag = -1 });

            for (int i = 0; i < displays.Count; i++)
            {
                var d = displays[i];
                var isPrimary = d.IsPrimary ? " (Primary)" : "";
                var label = $"Monitor {i + 1}{isPrimary}: {d.OuterBounds.Width}x{d.OuterBounds.Height}";
                ComboTargetDisplay.Items.Add(new ComboBoxItem { Content = label, Tag = i });
            }

            var savedIdx = _settingsService.Current.TargetDisplayIndex;
            int selectIndex = 0;
            for (int i = 0; i < ComboTargetDisplay.Items.Count; i++)
            {
                if (ComboTargetDisplay.Items[i] is ComboBoxItem cbi && cbi.Tag is int tag && tag == savedIdx)
                {
                    selectIndex = i;
                    break;
                }
            }
            ComboTargetDisplay.SelectedIndex = selectIndex;
            PopulatePerMonitorSources(displays);
        }
        catch (Exception ex)
        {
            App.Log($"UpdateScreenInfo error: {ex.Message}");
            TxtDetectedDisplays.Text = "1 Active Display";
        }
    }

    private void PopulatePerMonitorSources(IReadOnlyList<DisplayArea> displays)
    {
        if (StackPerMonitorCards == null || _mediaLibrary == null) return;
        StackPerMonitorCards.Children.Clear();

        if (displays == null || displays.Count <= 1)
        {
            if (PanelPerMonitorRouting != null) PanelPerMonitorRouting.Visibility = Visibility.Collapsed;
            return;
        }

        var sources = _mediaLibrary.GetMediaSources();

        for (int i = 0; i < displays.Count; i++)
        {
            int displayIndex = i;
            var d = displays[i];
            var card = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header = $"Monitor {i + 1}{(d.IsPrimary ? " (Primary)" : "")}",
                Description = $"{d.OuterBounds.Width}x{d.OuterBounds.Height} at ({d.OuterBounds.X},{d.OuterBounds.Y})"
            };

            var combo = new ComboBox { Width = 280 };
            combo.Items.Add(new ComboBoxItem { Content = "(Follow Global Active Source)", Tag = "" });

            int selectIndex = 0;
            string currentAssigned = _settingsService?.Current?.PerMonitorSourcePath != null &&
                                     _settingsService.Current.PerMonitorSourcePath.TryGetValue(displayIndex, out var p)
                ? p : "";

            for (int sIdx = 0; sIdx < sources.Count; sIdx++)
            {
                var src = sources[sIdx];
                var cbi = new ComboBoxItem
                {
                    Content = $"{src.DisplayName} ({src.ItemCountText})",
                    Tag = src.Path
                };
                combo.Items.Add(cbi);
                if (!string.IsNullOrEmpty(currentAssigned) && string.Equals(src.Path, currentAssigned, StringComparison.OrdinalIgnoreCase))
                {
                    selectIndex = combo.Items.Count - 1;
                }
            }

            combo.SelectedIndex = selectIndex;
            combo.SelectionChanged += (snd, args) =>
            {
                if (_isInitializing || _settingsService == null) return;
                if (combo.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string chosenPath)
                {
                    _settingsService.Current.PerMonitorSourcePath ??= new();
                    if (string.IsNullOrEmpty(chosenPath))
                    {
                        _settingsService.Current.PerMonitorSourcePath.Remove(displayIndex);
                    }
                    else
                    {
                        _settingsService.Current.PerMonitorSourcePath[displayIndex] = chosenPath;
                    }
                    _settingsService.Save();
                    App.Log($"Display {displayIndex} assigned source: {chosenPath}");
                }
            };

            card.Content = combo;
            StackPerMonitorCards.Children.Add(card);
        }
    }

    private void OnTargetDisplayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _settingsService == null) return;
        if (ComboTargetDisplay.SelectedItem is ComboBoxItem item && item.Tag is int targetIdx)
        {
            _settingsService.Current.TargetDisplayIndex = targetIdx;
            _settingsService.Save();
            UpdateControlStates();
            App.Log($"TargetDisplayIndex set to: {targetIdx}");
        }
    }

    public void UpdateSourceList()
    {
        if (_mediaLibrary == null || _settingsService == null) return;

        var dynSources = _mediaLibrary.GetDynamicThemeSources();
        var stdSources = _mediaLibrary.GetStandardFolderSources();

        ListDynamicThemes.ItemsSource = dynSources;
        ListStandardFolders.ItemsSource = stdSources;

        TxtThemeBadge.Text = $"{dynSources.Count} Theme(s)";
        TxtStandardFolderBadge.Text = $"{stdSources.Count} Folder(s)";

        var activePath = _settingsService.Current.ActiveSourcePath;
        if (string.IsNullOrEmpty(activePath) || string.Equals(activePath, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            TxtActiveSourceTitle.Text = $"All Sources Combined ({stdSources.Count} folders • {_mediaLibrary.TotalMediaCount} items)";
            BtnPlayAllCombined.Content = "✓ Active Combined";
            BtnPlayAllCombined.IsEnabled = false;
        }
        else
        {
            var matchTheme = dynSources.FirstOrDefault(s => string.Equals(s.Path, activePath, StringComparison.OrdinalIgnoreCase));
            if (matchTheme != null)
            {
                TxtActiveSourceTitle.Text = $"Dynamic Theme: {matchTheme.DisplayName} ({matchTheme.ItemCountText})";
            }
            else
            {
                var matchFolder = stdSources.FirstOrDefault(s => string.Equals(s.Path, activePath, StringComparison.OrdinalIgnoreCase));
                var name = matchFolder?.DisplayName ?? Path.GetFileName(activePath);
                var badge = matchFolder != null ? $"{matchFolder.BadgeText} • {matchFolder.ItemCountText}" : "Standard Folder";
                TxtActiveSourceTitle.Text = $"Folder: {name} ({badge})";
            }
            BtnPlayAllCombined.Content = "Play All (Combined)";
            BtnPlayAllCombined.IsEnabled = true;
        }

        UpdateControlStates();
        UpdateThemeStatusText();
        UpdateMiniMonitorWallpaper();
    }

    private void OnPlayAllCombinedClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null || _mediaLibrary == null) return;
        _settingsService.Current.ActiveSourcePath = "ALL";
        _settingsService.Save();
        _mediaLibrary.ResetSequenceIndex();
        UpdateSourceList();
        App.Log("ActiveSourcePath set to: ALL");
    }

    private void OnSetSourceActiveClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null || _mediaLibrary == null) return;
        if (sender is Button btn && btn.Tag is string path)
        {
            _settingsService.Current.ActiveSourcePath = path;

            if (path == "ALL")
            {
                _mediaLibrary.ResetSequenceIndex();
            }
            else
            {
                var theme = DynamicThemeService.TryLoadThemeFromFolder(path);
                if (theme != null)
                {
                    _settingsService.Current.DynamicThemeEnabled = true;
                    _settingsService.Current.SelectedThemeId = theme.Id;
                }
                else
                {
                    _settingsService.Current.DynamicThemeEnabled = false;
                }
                _mediaLibrary.ResetSequenceIndex();
            }

            _settingsService.Save();
            UpdateSourceList();
            App.Log($"ActiveSourcePath set to: {path}");
        }
    }

    private string? _editingSourcePath;

    private void OnRemoveSourceClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null || _mediaLibrary == null) return;
        if (sender is Button btn && btn.Tag is string path)
        {
            _settingsService.Current.MediaFolders.Remove(path);
            _settingsService.Current.FolderProfiles.Remove(path);
            if (string.Equals(_settingsService.Current.ActiveSourcePath, path, StringComparison.OrdinalIgnoreCase))
            {
                _settingsService.Current.ActiveSourcePath = "ALL";
            }
            _settingsService.Save();
            _mediaLibrary.Rescan();
        }
    }

    private async void OnSourceEffectsClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;

        string? path = null;
        if (sender is FrameworkElement elem && elem.Tag is string tagPath)
        {
            path = tagPath;
        }

        if (string.IsNullOrEmpty(path))
        {
            path = _settingsService.Current.ActiveSourcePath;
            if (string.IsNullOrEmpty(path)) path = "ALL";
        }

        _editingSourcePath = path;
        var profile = _settingsService.Current.GetProfileForPath(path);

        string displayName;
        if (string.Equals(path, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            displayName = "All Sources (Default Combined Profile)";
        }
        else
        {
            displayName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(displayName)) displayName = path;
        }

        TxtDialogSourceTitle.Text = displayName;
        TxtDialogSourcePath.Text = path;

        ComboDialogTransitionStyle.SelectedIndex = (int)profile.TransitionStyle;
        SliderDialogTransitionDuration.Value = profile.TransitionDurationSeconds;
        TxtDialogTransitionDuration.Text = $"{profile.TransitionDurationSeconds:F1}s";
        SliderDialogPhotoDuration.Value = profile.PhotoDisplaySeconds;
        TxtDialogPhotoDuration.Text = $"{profile.PhotoDisplaySeconds:F0}s";
        ToggleDialogKenBurns.IsOn = profile.KenBurnsEffect;
        ComboDialogFitMode.SelectedIndex = (int)profile.FitMode;

        DialogSourceEffects.XamlRoot = this.Content.XamlRoot;
        await DialogSourceEffects.ShowAsync();
    }

    private void OnDialogSettingChanged(object sender, object e)
    {
        if (TxtDialogTransitionDuration != null && SliderDialogTransitionDuration != null)
        {
            TxtDialogTransitionDuration.Text = $"{SliderDialogTransitionDuration.Value:F1}s";
        }
        if (TxtDialogPhotoDuration != null && SliderDialogPhotoDuration != null)
        {
            TxtDialogPhotoDuration.Text = $"{SliderDialogPhotoDuration.Value:F0}s";
        }
    }

    private void OnSourceEffectsDoneClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_settingsService == null || string.IsNullOrEmpty(_editingSourcePath)) return;

        var profile = new SourceProfile
        {
            TransitionStyle = ComboDialogTransitionStyle.SelectedIndex >= 0
                ? (TransitionType)ComboDialogTransitionStyle.SelectedIndex
                : TransitionType.Crossfade,
            TransitionDurationSeconds = SliderDialogTransitionDuration.Value,
            PhotoDisplaySeconds = SliderDialogPhotoDuration.Value,
            KenBurnsEffect = ToggleDialogKenBurns.IsOn,
            FitMode = ComboDialogFitMode.SelectedIndex >= 0
                ? (MediaFitMode)ComboDialogFitMode.SelectedIndex
                : MediaFitMode.Fill
        };

        if (string.Equals(_editingSourcePath, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            _settingsService.Current.TransitionStyle = profile.TransitionStyle;
            _settingsService.Current.TransitionDurationSeconds = profile.TransitionDurationSeconds;
            _settingsService.Current.PhotoDisplaySeconds = profile.PhotoDisplaySeconds;
            _settingsService.Current.KenBurnsEffect = profile.KenBurnsEffect;
            _settingsService.Current.FitMode = profile.FitMode;
        }

        _settingsService.Current.FolderProfiles[_editingSourcePath] = profile;
        _settingsService.Save();
        UpdateSourceList();
        App.Log($"Saved SourceProfile for {_editingSourcePath}: Style={profile.TransitionStyle}, Fit={profile.FitMode}");
    }

    private void OnSourceEffectsResetClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;

        var defaultProfile = new SourceProfile();
        ComboDialogTransitionStyle.SelectedIndex = (int)defaultProfile.TransitionStyle;
        SliderDialogTransitionDuration.Value = defaultProfile.TransitionDurationSeconds;
        TxtDialogTransitionDuration.Text = $"{defaultProfile.TransitionDurationSeconds:F1}s";
        SliderDialogPhotoDuration.Value = defaultProfile.PhotoDisplaySeconds;
        TxtDialogPhotoDuration.Text = $"{defaultProfile.PhotoDisplaySeconds:F0}s";
        ToggleDialogKenBurns.IsOn = defaultProfile.KenBurnsEffect;
        ComboDialogFitMode.SelectedIndex = (int)defaultProfile.FitMode;

        if (_settingsService != null && !string.IsNullOrEmpty(_editingSourcePath))
        {
            if (string.Equals(_editingSourcePath, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                _settingsService.Current.TransitionStyle = defaultProfile.TransitionStyle;
                _settingsService.Current.TransitionDurationSeconds = defaultProfile.TransitionDurationSeconds;
                _settingsService.Current.PhotoDisplaySeconds = defaultProfile.PhotoDisplaySeconds;
                _settingsService.Current.KenBurnsEffect = defaultProfile.KenBurnsEffect;
                _settingsService.Current.FitMode = defaultProfile.FitMode;
            }

            _settingsService.Current.FolderProfiles[_editingSourcePath] = defaultProfile;
            _settingsService.Save();
            UpdateSourceList();
            App.Log($"Reset SourceProfile for {_editingSourcePath} to defaults");
        }
    }

    private void UpdateMediaCountText()
    {
        UpdateSourceList();
    }

    public void UpdateControlStates()
    {
        if (_settingsService == null || _mediaLibrary == null) return;

        var activeTheme = _mediaLibrary.GetActiveTheme();
        bool isDynActive = activeTheme != null;

        if (PanelSolarConfig != null)
        {
            PanelSolarConfig.Visibility = isDynActive ? Visibility.Visible : Visibility.Collapsed;
        }

        CardThemeMode.IsEnabled = isDynActive;
        ComboThemeMode.IsEnabled = isDynActive;

        CardSolarSchedule.Opacity = isDynActive ? 1.0 : 0.5;
        CardSolarSchedule.IsHitTestVisible = isDynActive;
        ToggleCustomSolar.IsEnabled = isDynActive;
        TimePickerSunrise.IsEnabled = isDynActive && ToggleCustomSolar.IsOn;
        TimePickerSunset.IsEnabled = isDynActive && ToggleCustomSolar.IsOn;

        CardSegmentOverride.IsEnabled = isDynActive;
        ComboSegmentOverride.IsEnabled = isDynActive;

        CardStandardOrder.IsEnabled = !isDynActive;
        ComboPlaybackOrder.IsEnabled = !isDynActive;

        bool isAllDisplays = _settingsService.Current.TargetDisplayIndex == -1;
        bool isIndependentMulti = isAllDisplays && _settingsService.Current.DisplayMode == MonitorMode.Independent;
        if (CardDisplayMode != null)
        {
            CardDisplayMode.IsEnabled = isAllDisplays;
            ComboDisplayMode.IsEnabled = isAllDisplays;
        }

        if (PanelPerMonitorRouting != null)
        {
            PanelPerMonitorRouting.Visibility = isIndependentMulti ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ToggleWidgetsAllMonitors != null)
        {
            ToggleWidgetsAllMonitors.IsEnabled = ToggleWidgetsEnabled?.IsOn ?? false;
        }

        if (ToggleWidgetsEnabled != null)
        {
            bool wOn = ToggleWidgetsEnabled.IsOn;

            // Clock
            if (ExpanderClockWidget != null) ExpanderClockWidget.IsEnabled = wOn;
            if (ToggleClockWidget != null) ToggleClockWidget.IsEnabled = wOn;
            bool cOn = wOn && (ToggleClockWidget?.IsOn ?? false);
            if (ComboClockFont != null) ComboClockFont.IsEnabled = cOn;
            if (ComboClockColor != null) ComboClockColor.IsEnabled = cOn;
            if (ComboClockFormat != null) ComboClockFormat.IsEnabled = cOn;
            if (ToggleClockAmPm != null) ToggleClockAmPm.IsEnabled = cOn && (ComboClockFormat?.SelectedIndex == 0);
            if (ToggleClockSeconds != null) ToggleClockSeconds.IsEnabled = cOn;
            if (ToggleClockDate != null) ToggleClockDate.IsEnabled = cOn;
            if (ComboDateFormat != null) ComboDateFormat.IsEnabled = cOn && (ToggleClockDate?.IsOn ?? false);
            if (ComboDatePosition != null) ComboDatePosition.IsEnabled = cOn && (ToggleClockDate?.IsOn ?? false);
            if (ToggleGlassPill != null) ToggleGlassPill.IsEnabled = cOn;
            if (CardGlassOpacity != null) CardGlassOpacity.IsEnabled = cOn && (ToggleGlassPill?.IsOn ?? false);
            if (SliderClockScale != null) SliderClockScale.IsEnabled = cOn;
            if (CardWidgetAccentStyle != null) CardWidgetAccentStyle.IsEnabled = wOn;
            if (ComboWidgetAccentStyle != null) ComboWidgetAccentStyle.IsEnabled = wOn;
            if (BtnPresetLockScreenRow != null) BtnPresetLockScreenRow.IsEnabled = wOn;
            if (TogglePositionShuffle != null) TogglePositionShuffle.IsEnabled = cOn;
            if (CardShuffleInterval != null) CardShuffleInterval.IsEnabled = cOn && (TogglePositionShuffle?.IsOn ?? false);
            if (BadgeClockWidget != null) BadgeClockWidget.Opacity = cOn ? 1.0 : 0.45;
            if (MiniPreviewClockWidget != null) MiniPreviewClockWidget.Visibility = cOn ? Visibility.Visible : Visibility.Collapsed;

            // Weather
            if (ExpanderWeatherWidget != null) ExpanderWeatherWidget.IsEnabled = wOn;
            if (ToggleWeatherWidget != null) ToggleWeatherWidget.IsEnabled = wOn;
            bool weatherOn = wOn && (ToggleWeatherWidget?.IsOn ?? false);
            if (TxtWeatherCity != null) TxtWeatherCity.IsEnabled = weatherOn;
            if (ComboWeatherUnit != null) ComboWeatherUnit.IsEnabled = weatherOn;
            if (SliderWeatherScale != null) SliderWeatherScale.IsEnabled = weatherOn;
            if (BadgeWeatherWidget != null) BadgeWeatherWidget.Opacity = weatherOn ? 1.0 : 0.45;
            if (MiniPreviewWeatherWidget != null) MiniPreviewWeatherWidget.Visibility = weatherOn ? Visibility.Visible : Visibility.Collapsed;

            // Media
            if (ExpanderMediaWidget != null) ExpanderMediaWidget.IsEnabled = wOn;
            if (ToggleMediaWidget != null) ToggleMediaWidget.IsEnabled = wOn;
            bool mediaOn = wOn && (ToggleMediaWidget?.IsOn ?? false);
            if (SliderMediaScale != null) SliderMediaScale.IsEnabled = mediaOn;
            if (BadgeMediaWidget != null) BadgeMediaWidget.Opacity = mediaOn ? 1.0 : 0.45;
            if (MiniPreviewMediaWidget != null)
            {
                MiniPreviewMediaWidget.IsWidgetEnabled = mediaOn;
                MiniPreviewMediaWidget.ApplyVisibilityState();
            }

            // Performance
            if (ExpanderPerformanceWidget != null) ExpanderPerformanceWidget.IsEnabled = wOn;
            if (TogglePerformanceWidget != null) TogglePerformanceWidget.IsEnabled = wOn;
            bool perfOn = wOn && (TogglePerformanceWidget?.IsOn ?? false);
            if (SliderPerformanceScale != null) SliderPerformanceScale.IsEnabled = perfOn;
            if (BadgePerformanceWidget != null) BadgePerformanceWidget.Opacity = perfOn ? 1.0 : 0.45;
            if (MiniPreviewPerformanceWidget != null) MiniPreviewPerformanceWidget.Visibility = perfOn ? Visibility.Visible : Visibility.Collapsed;

            // Calendar
            if (ExpanderCalendarWidget != null) ExpanderCalendarWidget.IsEnabled = wOn;
            if (ToggleCalendarWidget != null) ToggleCalendarWidget.IsEnabled = wOn;
            bool calOn = wOn && (ToggleCalendarWidget?.IsOn ?? false);
            if (SliderCalendarScale != null) SliderCalendarScale.IsEnabled = calOn;
            if (BadgeCalendarWidget != null) BadgeCalendarWidget.Opacity = calOn ? 1.0 : 0.45;
            if (MiniPreviewCalendarWidget != null) MiniPreviewCalendarWidget.Visibility = calOn ? Visibility.Visible : Visibility.Collapsed;

            // Wisdom & Quotes
            if (ExpanderQuoteWidget != null) ExpanderQuoteWidget.IsEnabled = wOn;
            if (ToggleQuoteWidget != null) ToggleQuoteWidget.IsEnabled = wOn;
            bool quoteOn = wOn && (ToggleQuoteWidget?.IsOn ?? false);
            if (ComboQuoteColorTheme != null) ComboQuoteColorTheme.IsEnabled = quoteOn;
            if (SliderQuoteBgOpacity != null) SliderQuoteBgOpacity.IsEnabled = quoteOn;
            if (ToggleQuoteShowBorder != null) ToggleQuoteShowBorder.IsEnabled = quoteOn;
            if (ToggleQuoteBlurEffect != null) ToggleQuoteBlurEffect.IsEnabled = quoteOn;
            bool blurOn = quoteOn && (ToggleQuoteBlurEffect?.IsOn ?? false);
            if (ComboQuoteBackdropTheme != null) ComboQuoteBackdropTheme.IsEnabled = blurOn;
            if (SliderQuoteBlurIntensity != null) SliderQuoteBlurIntensity.IsEnabled = blurOn;
            if (ComboQuoteFontStyle != null) ComboQuoteFontStyle.IsEnabled = quoteOn;
            if (SliderQuoteMainFontSize != null) SliderQuoteMainFontSize.IsEnabled = quoteOn;
            if (SliderQuoteMeaningFontSize != null) SliderQuoteMeaningFontSize.IsEnabled = quoteOn;
            if (ComboQuoteAlignment != null) ComboQuoteAlignment.IsEnabled = quoteOn;
            if (SliderQuoteCardWidth != null) SliderQuoteCardWidth.IsEnabled = quoteOn;
            if (SliderQuoteScale != null) SliderQuoteScale.IsEnabled = quoteOn;
            if (ToggleQuoteShowCategory != null) ToggleQuoteShowCategory.IsEnabled = quoteOn;
            if (ToggleQuoteShowAuthor != null) ToggleQuoteShowAuthor.IsEnabled = quoteOn;
            if (ToggleQuoteShowMeaning != null) ToggleQuoteShowMeaning.IsEnabled = quoteOn;
            if (ComboQuoteRotation != null) ComboQuoteRotation.IsEnabled = quoteOn;
            if (TxtQuoteDbPath != null) TxtQuoteDbPath.IsEnabled = quoteOn;
            if (BtnBrowseQuoteDb != null) BtnBrowseQuoteDb.IsEnabled = quoteOn;
            if (BtnBrowseQuoteFolder != null) BtnBrowseQuoteFolder.IsEnabled = quoteOn;
            if (BtnNextQuotePreview != null) BtnNextQuotePreview.IsEnabled = quoteOn;
            if (BtnNextQuoteLivePreview != null) BtnNextQuoteLivePreview.IsEnabled = quoteOn;
            if (BtnResetQuoteDesign != null) BtnResetQuoteDesign.IsEnabled = quoteOn;
            if (LivePreviewQuoteCard != null) LivePreviewQuoteCard.Opacity = quoteOn ? 1.0 : 0.45;
            if (BadgeQuoteWidget != null) BadgeQuoteWidget.Opacity = quoteOn ? 1.0 : 0.45;
            if (MiniPreviewQuoteWidget != null) MiniPreviewQuoteWidget.Visibility = quoteOn ? Visibility.Visible : Visibility.Collapsed;

            bool anyWidgetPlaced = cOn || weatherOn || mediaOn || perfOn || calOn || quoteOn;
            if (PanelEmptyPlacementState != null) PanelEmptyPlacementState.Visibility = anyWidgetPlaced ? Visibility.Collapsed : Visibility.Visible;
            if (BtnRemovePlacement != null) BtnRemovePlacement.IsEnabled = anyWidgetPlaced;
            if (TxtStatusPlacement != null)
            {
                var activeList = new List<string>();
                if (cOn) activeList.Add("Clock");
                if (weatherOn) activeList.Add("Weather");
                if (mediaOn) activeList.Add("Media");
                if (perfOn) activeList.Add("Perf");
                if (calOn) activeList.Add("Calendar");
                if (quoteOn) activeList.Add("Quotes");
                TxtStatusPlacement.Text = anyWidgetPlaced ? $"{string.Join(", ", activeList)} Active" : "No Widgets Placed";
            }
        }
    }

    public void UpdateThemeStatusText()
    {
        if (_mediaLibrary == null || _settingsService == null || CardThemeStatus == null) return;

        var activeTheme = _mediaLibrary.GetActiveTheme();
        var s = _settingsService.Current;

        if (activeTheme == null)
        {
            if (BadgeActivePhase != null) BadgeActivePhase.Visibility = Visibility.Collapsed;
            if (BadgeFrameNum != null) BadgeFrameNum.Visibility = Visibility.Collapsed;
            if (PanelSolarChips != null) PanelSolarChips.Visibility = Visibility.Collapsed;
            if (TxtStandardDetails != null) TxtStandardDetails.Visibility = Visibility.Visible;

            var activePath = s.ActiveSourcePath;
            bool isAll = string.IsNullOrEmpty(activePath) || string.Equals(activePath, "ALL", StringComparison.OrdinalIgnoreCase);
            var title = isAll ? "All Sources (Combined)" : Path.GetFileName(activePath);
            if (TxtPreviewTitle != null) TxtPreviewTitle.Text = title;

            var activeItems = _mediaLibrary.GetActiveItems();
            var firstMedia = activeItems.FirstOrDefault();

            if (firstMedia != null && File.Exists(firstMedia.FilePath))
            {
                if (TxtActiveFileName != null) TxtActiveFileName.Text = firstMedia.FileName;
                if (TxtActiveTimeWindow != null) TxtActiveTimeWindow.Text = $"{activeItems.Count} items in playlist";
                if (TxtStandardDetails != null) TxtStandardDetails.Text = $"Standard Playback • Progression order: {s.Order}";

                try
                {
                    if (firstMedia.Type == MediaType.Image && ImgWallpaperPreview != null)
                    {
                        ImgWallpaperPreview.Source = new BitmapImage(new Uri(firstMedia.FilePath));
                        ImgWallpaperPreview.Visibility = Visibility.Visible;
                        if (IconPreviewFallback != null) IconPreviewFallback.Visibility = Visibility.Collapsed;
                    }
                    else if (IconPreviewFallback != null)
                    {
                        if (ImgWallpaperPreview != null) ImgWallpaperPreview.Visibility = Visibility.Collapsed;
                        IconPreviewFallback.Glyph = "\uE714";
                        IconPreviewFallback.Visibility = Visibility.Visible;
                    }
                }
                catch
                {
                    if (ImgWallpaperPreview != null) ImgWallpaperPreview.Visibility = Visibility.Collapsed;
                    if (IconPreviewFallback != null) IconPreviewFallback.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (TxtActiveFileName != null) TxtActiveFileName.Text = "No media files found";
                if (TxtActiveTimeWindow != null) TxtActiveTimeWindow.Text = "Folder empty";
                if (TxtStandardDetails != null) TxtStandardDetails.Text = "Add photos or videos to start screensaver playback.";
                if (ImgWallpaperPreview != null) ImgWallpaperPreview.Visibility = Visibility.Collapsed;
                if (IconPreviewFallback != null)
                {
                    IconPreviewFallback.Glyph = "\uE8B7";
                    IconPreviewFallback.Visibility = Visibility.Visible;
                }
            }
            return;
        }

        if (BadgeActivePhase != null) BadgeActivePhase.Visibility = Visibility.Visible;
        if (BadgeFrameNum != null) BadgeFrameNum.Visibility = Visibility.Visible;
        if (PanelSolarChips != null) PanelSolarChips.Visibility = Visibility.Visible;
        if (TxtStandardDetails != null) TxtStandardDetails.Visibility = Visibility.Collapsed;

        var times = SolarCalculator.GetSolarTimes(DateTime.Now, activeTheme.SunriseImages.Count > 0, activeTheme.SunsetImages.Count > 0, s);
        var (segment, imageId, item) = SolarCalculator.GetCurrentSolarMedia(activeTheme, DateTime.Now, s);

        if (TxtPreviewTitle != null) TxtPreviewTitle.Text = activeTheme.DisplayName;

        string phaseText = segment switch
        {
            SolarSegment.Sunrise => "Sunrise",
            SolarSegment.Day => "Day",
            SolarSegment.Sunset => "Sunset",
            SolarSegment.Night => "Night",
            _ => "Solar Active"
        };
        if (TxtActivePhase != null) TxtActivePhase.Text = phaseText;

        int totalFrames = activeTheme.ImageMap.Count;
        if (TxtFrameNum != null) TxtFrameNum.Text = $"Frame #{imageId} of {totalFrames}";

        var fileName = item?.FileName ?? $"#{imageId}";
        if (TxtActiveFileName != null) TxtActiveFileName.Text = fileName;

        string activeWindow = segment switch
        {
            SolarSegment.Sunrise => $"Window: {times.SunriseStart:h:mm tt} – {times.SunriseEnd:h:mm tt}",
            SolarSegment.Day => $"Window: {times.SunriseEnd:h:mm tt} – {times.SunsetStart:h:mm tt}",
            SolarSegment.Sunset => $"Window: {times.SunsetStart:h:mm tt} – {times.SunsetEnd:h:mm tt}",
            SolarSegment.Night => $"Window: {times.SunsetEnd:h:mm tt} – {times.SunriseStart:h:mm tt}",
            _ => ""
        };
        if (TxtActiveTimeWindow != null) TxtActiveTimeWindow.Text = activeWindow;

        if (item != null && File.Exists(item.FilePath))
        {
            try
            {
                if (ImgWallpaperPreview != null)
                {
                    ImgWallpaperPreview.Source = new BitmapImage(new Uri(item.FilePath));
                    ImgWallpaperPreview.Visibility = Visibility.Visible;
                }
                if (IconPreviewFallback != null) IconPreviewFallback.Visibility = Visibility.Collapsed;
            }
            catch
            {
                if (ImgWallpaperPreview != null) ImgWallpaperPreview.Visibility = Visibility.Collapsed;
                if (IconPreviewFallback != null)
                {
                    IconPreviewFallback.Glyph = "\uE706";
                    IconPreviewFallback.Visibility = Visibility.Visible;
                }
            }
        }
        else
        {
            if (ImgWallpaperPreview != null) ImgWallpaperPreview.Visibility = Visibility.Collapsed;
            if (IconPreviewFallback != null)
            {
                IconPreviewFallback.Glyph = "\uE706";
                IconPreviewFallback.Visibility = Visibility.Visible;
            }
        }

        bool hasSr = activeTheme.SunriseImages.Count > 0;
        bool hasSs = activeTheme.SunsetImages.Count > 0;

        if (ChipSunrise != null) ChipSunrise.Visibility = hasSr ? Visibility.Visible : Visibility.Collapsed;
        if (ChipSunset != null) ChipSunset.Visibility = hasSs ? Visibility.Visible : Visibility.Collapsed;

        if (TxtChipSunrise != null) TxtChipSunrise.Text = $"Sunrise ({times.SunriseStart:h:mm tt}–{times.SunriseEnd:h:mm tt})";
        if (TxtChipDay != null) TxtChipDay.Text = $"Day ({times.SunriseEnd:h:mm tt}–{times.SunsetStart:h:mm tt})";
        if (TxtChipSunset != null) TxtChipSunset.Text = $"Sunset ({times.SunsetStart:h:mm tt}–{times.SunsetEnd:h:mm tt})";
        if (TxtChipNight != null) TxtChipNight.Text = $"Night ({times.SunsetEnd:h:mm tt}–{times.SunriseStart:h:mm tt})";

        UpdateChipHighlight(ChipSunrise, TxtChipSunrise, segment == SolarSegment.Sunrise);
        UpdateChipHighlight(ChipDay, TxtChipDay, segment == SolarSegment.Day);
        UpdateChipHighlight(ChipSunset, TxtChipSunset, segment == SolarSegment.Sunset);
        UpdateChipHighlight(ChipNight, TxtChipNight, segment == SolarSegment.Night);
    }

    private void UpdateChipHighlight(Border? chip, TextBlock? txt, bool isCurrent)
    {
        if (chip == null || txt == null) return;
        try
        {
            if (isCurrent)
            {
                if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var bg) && bg is Brush accentBg)
                    chip.Background = accentBg;
                if (Application.Current.Resources.TryGetValue("TextOnAccentFillColorPrimaryBrush", out var fg) && fg is Brush accentFg)
                    txt.Foreground = accentFg;
                txt.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorSecondaryBrush", out var bg) && bg is Brush cardBg)
                    chip.Background = cardBg;
                if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out var fg) && fg is Brush textFg)
                    txt.Foreground = textFg;
                txt.FontWeight = FontWeights.Normal;
            }
        }
        catch { }
    }

    private void OnSolarTimePickerChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (_isInitializing || _settingsService == null) return;
        var s = _settingsService.Current;
        s.CustomSunriseTime = TimePickerSunrise.Time.ToString(@"hh\:mm");
        s.CustomSunsetTime = TimePickerSunset.Time.ToString(@"hh\:mm");
        _settingsService.Save();
        UpdateThemeStatusText();
    }

    private void OnImportThemeClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null || _mediaLibrary == null) return;
        var filter = "Dynamic Theme Packages (*.lsdt;*.ldt;*.zip;*.json;*.ddw)\0*.lsdt;*.ldt;*.zip;*.json;*.ddw\0All Files (*.*)\0*.*\0\0";
        var filePath = NativeMethods.PickFile(_windowHandle, "Import Dynamic Theme (.lsdt, .zip, or theme.json)", filter);

        if (!string.IsNullOrEmpty(filePath))
        {
            var importedDir = DynamicThemeService.ImportThemeFile(filePath);
            if (!string.IsNullOrEmpty(importedDir))
            {
                if (!_settingsService.Current.MediaFolders.Contains(importedDir))
                {
                    _settingsService.Current.MediaFolders.Add(importedDir);
                }
                var loadedTheme = DynamicThemeService.TryLoadThemeFromFolder(importedDir);
                if (loadedTheme != null)
                {
                    _settingsService.Current.DynamicThemeEnabled = true;
                    _settingsService.Current.SelectedThemeId = loadedTheme.Id;
                    _settingsService.Current.ActiveSourcePath = importedDir;
                }
                _settingsService.Save();
                UpdateSourceList();
                _mediaLibrary.Rescan();
            }
        }
    }

    private void OnExportThemeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path && Directory.Exists(path))
        {
            var theme = DynamicThemeService.TryLoadThemeFromFolder(path);
            var name = theme?.DisplayName ?? Path.GetFileName(path);
            var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            var filter = "LivelyScreen Dynamic Theme (*.lsdt)\0*.lsdt\0All Files (*.*)\0*.*\0\0";
            var savePath = NativeMethods.PickSaveFile(_windowHandle, "Export Dynamic Theme Package (.lsdt)", filter, "lsdt", $"{safeName}.lsdt");
            if (!string.IsNullOrEmpty(savePath))
            {
                bool ok = DynamicThemeService.ExportThemePackage(path, savePath);
                if (ok)
                {
                    App.Log($"Dynamic theme exported: {savePath}");
                }
            }
        }
    }

    private void OnIdlePresetClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag != null && double.TryParse(btn.Tag.ToString(), out var mins))
        {
            SliderIdleMinutes.Value = mins;
        }
    }

    private void OnSettingChanged(object sender, object e)
    {
        if (_isInitializing || _settingsService == null || _mediaLibrary == null) return;

        var s = _settingsService.Current;
        s.IdleDelayMinutes = (int)SliderIdleMinutes.Value;
        TxtIdleMinutes.Text = $"{s.IdleDelayMinutes} min";

        s.SmartPauseInFullscreen = ToggleSmartPause.IsOn;

        if (ComboDisplayMode.SelectedIndex >= 0)
        {
            s.DisplayMode = (MonitorMode)ComboDisplayMode.SelectedIndex;
        }

        if (ComboPlaybackOrder.SelectedIndex >= 0)
        {
            s.Order = (PlaybackOrder)ComboPlaybackOrder.SelectedIndex;
            _mediaLibrary.SortItems(s.Order);
        }

        if (ComboThemeMode.SelectedIndex >= 0)
        {
            s.ThemeMode = (DynamicThemeMode)ComboThemeMode.SelectedIndex;
            _mediaLibrary.ResetSequenceIndex();
        }

        s.UseCustomSolarTimes = ToggleCustomSolar.IsOn;

        if (ComboSegmentOverride.SelectedIndex >= 0)
        {
            s.SegmentOverride = (SolarSegmentOverride)ComboSegmentOverride.SelectedIndex;
            _mediaLibrary.ResetSequenceIndex();
        }

        UpdateControlStates();
        UpdateThemeStatusText();

        s.VideoAudioEnabled = ToggleAudio.IsOn;
        SliderVolume.IsEnabled = s.VideoAudioEnabled;
        s.VideoVolumePercent = (int)SliderVolume.Value;
        TxtVolume.Text = $"{s.VideoVolumePercent}%";

        if (ComboVisualFilter != null && ComboVisualFilter.SelectedIndex >= 0)
            s.VisualFilter = (VisualFilterPreset)ComboVisualFilter.SelectedIndex;

        if (SliderBrightnessDim != null)
        {
            s.BrightnessDimPercent = (int)SliderBrightnessDim.Value;
            if (TxtBrightnessDim != null) TxtBrightnessDim.Text = $"{s.BrightnessDimPercent}%";
        }

        if (ToggleMediaInfoPill != null)
            s.ShowMediaInfoPill = ToggleMediaInfoPill.IsOn;

        if (ComboAmbientSound != null && ComboAmbientSound.SelectedIndex >= 0)
            s.AmbientSoundMode = (AmbientSoundscapeMode)ComboAmbientSound.SelectedIndex;

        if (SliderAmbientVolume != null)
        {
            s.AmbientSoundVolume = (int)SliderAmbientVolume.Value;
            if (TxtAmbientVolume != null) TxtAmbientVolume.Text = $"{s.AmbientSoundVolume}%";
        }

        if (TxtAmbientCustomPath != null)
            s.AmbientCustomAudioPath = TxtAmbientCustomPath.Text?.Trim() ?? "";

        if (ToggleAmbientShuffle != null)
            s.AmbientMusicShuffle = ToggleAmbientShuffle.IsOn;

        s.MouseSensitivityPixels = (int)SliderSensitivity.Value;
        TxtSensitivity.Text = $"{s.MouseSensitivityPixels} px";

        s.AllowDisplaySleep = ToggleDisplaySleep.IsOn;
        s.OledPixelShiftEnabled = ToggleOledShift.IsOn;
        s.AutoBlackoutMinutes = ComboAutoBlackout.SelectedIndex switch
        {
            1 => 5,
            2 => 10,
            3 => 15,
            4 => 30,
            5 => 60,
            _ => 0
        };

        if (ComboTriggerCorner != null && ComboTriggerCorner.SelectedIndex >= 0)
            s.TriggerCorner = (HotCornerZone)ComboTriggerCorner.SelectedIndex;
        if (ComboDisableCorner != null && ComboDisableCorner.SelectedIndex >= 0)
            s.DisableCorner = (HotCornerZone)ComboDisableCorner.SelectedIndex;
        if (TogglePauseOnBattery != null)
            s.PauseOnBattery = TogglePauseOnBattery.IsOn;

        if (ComboAppTheme.SelectedIndex >= 0)
        {
            var newTheme = (AppThemeMode)ComboAppTheme.SelectedIndex;
            if (s.ThemePreference != newTheme)
            {
                s.ThemePreference = newTheme;
                ThemePreferenceChanged?.Invoke();
            }
        }

        s.WidgetsEnabled = ToggleWidgetsEnabled.IsOn;
        s.ShowWidgetsOnAllMonitors = ToggleWidgetsAllMonitors?.IsOn ?? false;
        s.ClockWidgetEnabled = ToggleClockWidget.IsOn;
        s.ClockFontFamily = ComboClockFont.SelectedIndex switch
        {
            1 => "SF Pro Display",
            2 => "Nunito",
            3 => "Bahnschrift",
            4 => "Segoe UI",
            _ => "Segoe UI Variable Display"
        };
        s.ClockColorPreset = ComboClockColor.SelectedIndex switch
        {
            1 => "Ice Blue",
            2 => "Soft Amber",
            3 => "Mint",
            4 => "Lavender",
            _ => "Crisp White"
        };
        s.ClockUse12Hour = ComboClockFormat.SelectedIndex == 0;
        s.ClockShowAmPm = ToggleClockAmPm.IsOn;
        s.ClockShowSeconds = ToggleClockSeconds.IsOn;
        s.ClockShowDate = ToggleClockDate.IsOn;

        if (ComboDateFormat.SelectedIndex >= 0)
        {
            s.DateFormat = (ClockDateFormat)ComboDateFormat.SelectedIndex;
        }

        s.DatePosition = ComboDatePosition.SelectedIndex == 1 ? ClockDatePosition.AboveClock : ClockDatePosition.BelowClock;
        s.ShowGlassPill = ToggleGlassPill.IsOn;
        s.GlassPillOpacity = SliderGlassOpacity.Value / 100.0;
        TxtGlassOpacity.Text = $"{SliderGlassOpacity.Value:F0}%";
        s.ClockScale = SliderClockScale.Value / 100.0;
        TxtClockScale.Text = $"{SliderClockScale.Value:F0}%";
        s.ClockShuffleEnabled = TogglePositionShuffle.IsOn;
        s.ClockShuffleSeconds = ComboShuffleInterval.SelectedIndex switch
        {
            1 => 30,
            2 => 60,
            3 => 300,
            _ => 10
        };
        if (ComboWidgetAccentStyle != null)
        {
            s.WidgetUseSystemAccent = ComboWidgetAccentStyle.SelectedIndex == 1;
        }

        // Weather Settings
        if (ToggleWeatherWidget != null) s.WeatherWidgetEnabled = ToggleWeatherWidget.IsOn;
        if (TxtWeatherCity != null) s.WeatherCity = string.IsNullOrWhiteSpace(TxtWeatherCity.Text) ? "Auto" : TxtWeatherCity.Text.Trim();
        if (ComboWeatherUnit != null) s.WeatherUseFahrenheit = ComboWeatherUnit.SelectedIndex == 1;
        if (SliderWeatherScale != null)
        {
            s.WeatherScale = SliderWeatherScale.Value / 100.0;
            if (TxtWeatherScale != null) TxtWeatherScale.Text = $"{SliderWeatherScale.Value:F0}%";
        }

        // Media Settings
        if (ToggleMediaWidget != null) s.MediaWidgetEnabled = ToggleMediaWidget.IsOn;
        if (SliderMediaScale != null)
        {
            s.MediaScale = SliderMediaScale.Value / 100.0;
            if (TxtMediaScale != null) TxtMediaScale.Text = $"{SliderMediaScale.Value:F0}%";
        }
        if (ToggleMediaAlbumArt != null) s.MediaShowAlbumArt = ToggleMediaAlbumArt.IsOn;
        if (ToggleMediaHideWhenIdle != null) s.MediaHideWhenIdle = ToggleMediaHideWhenIdle.IsOn;
        if (TxtMediaIdleText != null) s.MediaIdleText = string.IsNullOrWhiteSpace(TxtMediaIdleText.Text) ? "Ready for Music" : TxtMediaIdleText.Text.Trim();
        if (ToggleAudioSpectrum != null) s.AudioSpectrumEnabled = ToggleAudioSpectrum.IsOn;

        // Performance Settings
        if (TogglePerformanceWidget != null) s.PerformanceWidgetEnabled = TogglePerformanceWidget.IsOn;
        if (SliderPerformanceScale != null)
        {
            s.PerformanceScale = SliderPerformanceScale.Value / 100.0;
            if (TxtPerformanceScale != null) TxtPerformanceScale.Text = $"{SliderPerformanceScale.Value:F0}%";
        }
        if (TogglePerfShowCpu != null) s.PerformanceShowCpu = TogglePerfShowCpu.IsOn;
        if (TogglePerfShowRam != null) s.PerformanceShowRam = TogglePerfShowRam.IsOn;
        if (TogglePerfPercentages != null) s.PerformanceShowPercentages = TogglePerfPercentages.IsOn;
        if (ComboPerfThreshold != null)
        {
            s.PerformanceHighlightThreshold = ComboPerfThreshold.SelectedIndex switch
            {
                0 => 80,
                1 => 85,
                2 => 90,
                3 => 95,
                _ => 101 // disabled
            };
        }
        if (ComboPerfFrequency != null)
        {
            s.PerformanceRefreshIntervalSeconds = ComboPerfFrequency.SelectedIndex switch
            {
                0 => 1,
                2 => 5,
                _ => 2
            };
        }

        // Calendar Settings
        if (ToggleCalendarWidget != null) s.CalendarWidgetEnabled = ToggleCalendarWidget.IsOn;
        if (SliderCalendarScale != null)
        {
            s.CalendarScale = SliderCalendarScale.Value / 100.0;
            if (TxtCalendarScale != null) TxtCalendarScale.Text = $"{SliderCalendarScale.Value:F0}%";
        }
        if (ComboCalendarFormat != null && ComboCalendarFormat.SelectedIndex >= 0)
        {
            s.CalendarDateFormat = ComboCalendarFormat.SelectedIndex;
        }
        if (ToggleCalendarWeekNumber != null) s.CalendarShowWeekNumber = ToggleCalendarWeekNumber.IsOn;
        if (ToggleCalendarShowEventNote != null) s.CalendarShowEventNote = ToggleCalendarShowEventNote.IsOn;
        if (TxtCalendarCustomNote != null) s.CalendarCustomNote = string.IsNullOrWhiteSpace(TxtCalendarCustomNote.Text) ? "No upcoming calendar conflicts" : TxtCalendarCustomNote.Text.Trim();
        if (ToggleCalendarLiveEvents != null) s.CalendarUseLiveEvents = ToggleCalendarLiveEvents.IsOn;
        if (TxtCalendarIcsPath != null) s.CalendarIcsUrlOrPath = TxtCalendarIcsPath.Text?.Trim() ?? "";

        // Wisdom & Quotes Settings
        if (ToggleQuoteWidget != null) s.QuoteWidgetEnabled = ToggleQuoteWidget.IsOn;
        if (TxtQuoteDbPath != null)
        {
            s.QuoteDatabasePath = string.IsNullOrWhiteSpace(TxtQuoteDbPath.Text)
                ? string.Empty
                : TxtQuoteDbPath.Text.Trim().Trim('"');
        }
        if (ComboQuoteColorTheme != null)
        {
            s.QuoteColorTheme = ComboQuoteColorTheme.SelectedIndex switch
            {
                1 => "Crisp White & Silver",
                2 => "Soft Lavender & Moonlight",
                3 => "Sage & Parchment",
                4 => "Windows System Accent",
                _ => "Warm Cream & Muted Gold"
            };
        }
        if (SliderQuoteBgOpacity != null)
        {
            s.QuoteBackgroundOpacity = SliderQuoteBgOpacity.Value / 100.0;
            if (TxtQuoteBgOpacity != null) TxtQuoteBgOpacity.Text = $"{SliderQuoteBgOpacity.Value:F0}%";
        }
        if (ToggleQuoteShowBorder != null) s.QuoteShowBorder = ToggleQuoteShowBorder.IsOn;
        if (ToggleQuoteBlurEffect != null) s.QuoteBlurEffect = ToggleQuoteBlurEffect.IsOn;
        if (ComboQuoteBackdropTheme != null)
        {
            s.QuoteBackdropTheme = ComboQuoteBackdropTheme.SelectedIndex switch
            {
                1 => "Frosted Blur",
                2 => "Mica Satin",
                _ => "Acrylic"
            };
        }
        if (SliderQuoteBlurIntensity != null)
        {
            s.QuoteBlurIntensity = SliderQuoteBlurIntensity.Value;
            if (TxtQuoteBlurIntensity != null) TxtQuoteBlurIntensity.Text = $"{SliderQuoteBlurIntensity.Value:F0} px";
        }
        if (ComboQuoteFontStyle != null)
        {
            s.QuoteFontStyle = ComboQuoteFontStyle.SelectedIndex switch
            {
                1 => "Modern Display Sans",
                2 => "Classic Book Antiqua",
                3 => "Monospace",
                _ => "Literary Serif"
            };
        }
        if (SliderQuoteMainFontSize != null)
        {
            s.QuoteMainFontSize = SliderQuoteMainFontSize.Value;
            if (TxtQuoteMainFontSize != null) TxtQuoteMainFontSize.Text = $"{SliderQuoteMainFontSize.Value:F0} pt";
        }
        if (SliderQuoteMeaningFontSize != null)
        {
            s.QuoteMeaningFontSize = SliderQuoteMeaningFontSize.Value;
            if (TxtQuoteMeaningFontSize != null) TxtQuoteMeaningFontSize.Text = $"{SliderQuoteMeaningFontSize.Value:0.#} pt";
        }
        if (ComboQuoteAlignment != null && ComboQuoteAlignment.SelectedIndex >= 0)
        {
            s.QuoteTextAlignment = ComboQuoteAlignment.SelectedIndex;
        }
        if (SliderQuoteCardWidth != null)
        {
            s.QuoteCardWidth = SliderQuoteCardWidth.Value;
            if (TxtQuoteCardWidth != null) TxtQuoteCardWidth.Text = $"{SliderQuoteCardWidth.Value:F0} px";
        }
        if (SliderQuoteScale != null)
        {
            s.QuoteScale = SliderQuoteScale.Value / 100.0;
            if (TxtQuoteScale != null) TxtQuoteScale.Text = $"{SliderQuoteScale.Value:F0}%";
        }
        if (ToggleQuoteShowCategory != null) s.QuoteShowCategoryHeader = ToggleQuoteShowCategory.IsOn;
        if (ToggleQuoteShowAuthor != null) s.QuoteShowAuthor = ToggleQuoteShowAuthor.IsOn;
        if (ToggleQuoteShowMeaning != null) s.QuoteShowMeaning = ToggleQuoteShowMeaning.IsOn;
        if (ComboQuoteRotation != null)
        {
            s.QuoteRotationIntervalSeconds = ComboQuoteRotation.SelectedIndex switch
            {
                0 => 15,
                1 => 30,
                2 => 60,
                3 => 300,
                4 => 0,
                _ => 30
            };
        }
        UpdateQuoteDbStatusUI(s.QuoteDatabasePath);

        UpdateControlStates();
        UpdateAllWidgetPositionPreviews();
        UpdateMiniMonitorWallpaper();

        _settingsService.Save();
    }

    private void OnBlockedProcessesTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _settingsService?.Current == null) return;
        var text = TxtBlockedProcesses?.Text ?? "";
        var list = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Where(p => !string.IsNullOrWhiteSpace(p))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToList();
        _settingsService.Current.BlockedProcesses = list;
        _settingsService.Save();
    }

    private void OnBrowseAmbientFolderClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        var folder = NativeMethods.PickFolder(_windowHandle, "Select Lo-Fi / Music Folder (.flac, .mp3, etc.)");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            if (TxtAmbientCustomPath != null) TxtAmbientCustomPath.Text = folder;
            if (ComboAmbientSound != null) ComboAmbientSound.SelectedIndex = (int)AmbientSoundscapeMode.CustomMusicFolder;
            _settingsService.Current.AmbientCustomAudioPath = folder;
            _settingsService.Current.AmbientSoundMode = AmbientSoundscapeMode.CustomMusicFolder;
            _settingsService.Save();
        }
    }

    private void OnBrowseAmbientAudioClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        var filter = "Audio Files (*.flac;*.mp3;*.wav;*.m4a;*.aac;*.ogg;*.wma)\0*.flac;*.mp3;*.wav;*.m4a;*.aac;*.ogg;*.wma\0All Files (*.*)\0*.*\0\0";
        var file = NativeMethods.PickFile(_windowHandle, "Select Ambient Soundscape Audio", filter);
        if (!string.IsNullOrWhiteSpace(file))
        {
            if (TxtAmbientCustomPath != null) TxtAmbientCustomPath.Text = file;
            if (ComboAmbientSound != null) ComboAmbientSound.SelectedIndex = (int)AmbientSoundscapeMode.CustomMusicFolder;
            _settingsService.Current.AmbientCustomAudioPath = file;
            _settingsService.Current.AmbientSoundMode = AmbientSoundscapeMode.CustomMusicFolder;
            _settingsService.Save();
        }
    }

    private void UpdateQuoteDbStatusUI(string? dbPath, bool forceReload = false)
    {
        try
        {
            QuoteService.EnsureQuotesLoaded(dbPath, forceReload);
            if (TxtQuoteDbStatus != null)
            {
                TxtQuoteDbStatus.Text = QuoteService.LastStatusMessage;
            }
            if (IconQuoteDbStatus != null)
            {
                IconQuoteDbStatus.Glyph = QuoteService.UsingFallbackCache ? "\uE7BA" : "\uE73E";
                IconQuoteDbStatus.Foreground = QuoteService.UsingFallbackCache
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 200, 76))
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 205, 255));
            }
        }
        catch { }
    }

    private void OnBrowseQuoteDbClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        var filter = "SQLite Database (*.db;*.sqlite;*.sqlite3)\0*.db;*.sqlite;*.sqlite3\0All Files (*.*)\0*.*\0\0";
        var filePath = NativeMethods.PickFile(_windowHandle, "Select KnowledgeBase SQLite Database (brain.db)", filter);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            if (TxtQuoteDbPath != null) TxtQuoteDbPath.Text = filePath;
            _settingsService.Current.QuoteDatabasePath = filePath;
            _settingsService.Save();
            UpdateQuoteDbStatusUI(filePath, true);
            LivePreviewQuoteCard?.ShowNextQuoteImmediate(true);
            MiniPreviewQuoteWidget?.ShowNextQuoteImmediate(true);
            UpdateAllWidgetPositionPreviews();
        }
    }

    private void OnBrowseQuoteFolderClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        var folderPath = NativeMethods.PickFolder(_windowHandle, "Select KnowledgeBase-Quotes Folder");
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            var resolved = QuoteService.ResolveDatabasePath(folderPath);
            if (TxtQuoteDbPath != null) TxtQuoteDbPath.Text = resolved;
            _settingsService.Current.QuoteDatabasePath = resolved;
            _settingsService.Save();
            UpdateQuoteDbStatusUI(resolved, true);
            LivePreviewQuoteCard?.ShowNextQuoteImmediate(true);
            MiniPreviewQuoteWidget?.ShowNextQuoteImmediate(true);
            UpdateAllWidgetPositionPreviews();
        }
    }

    private void OnNextQuotePreviewClicked(object sender, RoutedEventArgs e)
    {
        var dbPath = _settingsService?.Current?.QuoteDatabasePath;
        UpdateQuoteDbStatusUI(dbPath, true);
        LivePreviewQuoteCard?.ShowNextQuoteImmediate(true);
        MiniPreviewQuoteWidget?.ShowNextQuoteImmediate(true);
    }

    private void OnResetQuoteDesignClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService?.Current == null) return;
        var s = _settingsService.Current;

        s.QuoteColorTheme = "Warm Cream & Muted Gold";
        s.QuoteBackgroundOpacity = 0.85;
        s.QuoteShowBorder = true;
        s.QuoteBlurEffect = true;
        s.QuoteBlurIntensity = 18.0;
        s.QuoteBackdropTheme = "Acrylic";
        s.QuoteFontStyle = "Literary Serif";
        s.QuoteMainFontSize = 16.0;
        s.QuoteMeaningFontSize = 11.5;
        s.QuoteTextAlignment = 0;
        s.QuoteCardWidth = 340.0;
        s.QuoteScale = 1.0;
        s.QuoteShowCategoryHeader = true;
        s.QuoteShowAuthor = true;
        s.QuoteShowMeaning = true;

        _isInitializing = true;
        try
        {
            if (ComboQuoteColorTheme != null) ComboQuoteColorTheme.SelectedIndex = 0;
            if (SliderQuoteBgOpacity != null) SliderQuoteBgOpacity.Value = 85;
            if (TxtQuoteBgOpacity != null) TxtQuoteBgOpacity.Text = "85%";
            if (ToggleQuoteShowBorder != null) ToggleQuoteShowBorder.IsOn = true;
            if (ToggleQuoteBlurEffect != null) ToggleQuoteBlurEffect.IsOn = true;
            if (ComboQuoteBackdropTheme != null) ComboQuoteBackdropTheme.SelectedIndex = 0;
            if (SliderQuoteBlurIntensity != null) SliderQuoteBlurIntensity.Value = 18;
            if (TxtQuoteBlurIntensity != null) TxtQuoteBlurIntensity.Text = "18 px";
            if (ComboQuoteFontStyle != null) ComboQuoteFontStyle.SelectedIndex = 0;
            if (SliderQuoteMainFontSize != null) SliderQuoteMainFontSize.Value = 16;
            if (TxtQuoteMainFontSize != null) TxtQuoteMainFontSize.Text = "16 pt";
            if (SliderQuoteMeaningFontSize != null) SliderQuoteMeaningFontSize.Value = 11.5;
            if (TxtQuoteMeaningFontSize != null) TxtQuoteMeaningFontSize.Text = "11.5 pt";
            if (ComboQuoteAlignment != null) ComboQuoteAlignment.SelectedIndex = 0;
            if (SliderQuoteCardWidth != null) SliderQuoteCardWidth.Value = 340;
            if (TxtQuoteCardWidth != null) TxtQuoteCardWidth.Text = "340 px";
            if (SliderQuoteScale != null) SliderQuoteScale.Value = 100;
            if (TxtQuoteScale != null) TxtQuoteScale.Text = "100%";
            if (ToggleQuoteShowCategory != null) ToggleQuoteShowCategory.IsOn = true;
            if (ToggleQuoteShowAuthor != null) ToggleQuoteShowAuthor.IsOn = true;
            if (ToggleQuoteShowMeaning != null) ToggleQuoteShowMeaning.IsOn = true;
        }
        finally
        {
            _isInitializing = false;
        }

        _settingsService.Save();
        UpdateAllWidgetPositionPreviews();
    }

    private void OnPresetLockScreenRowClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService?.Current == null) return;
        var s = _settingsService.Current;

        s.WidgetsEnabled = true;
        s.ClockWidgetEnabled = true;
        s.ClockPosition = WidgetPosition.TopCenter;

        // Literary Quotes stays independent at Center, separate from the Bottom-Center utility dock
        s.QuoteWidgetEnabled = true;
        s.QuotePosition = WidgetPosition.Center;

        s.WeatherWidgetEnabled = true;
        s.WeatherPosition = WidgetPosition.BottomCenter;

        s.CalendarWidgetEnabled = true;
        s.CalendarPosition = WidgetPosition.BottomCenter;

        s.MediaWidgetEnabled = true;
        s.MediaPosition = WidgetPosition.BottomCenter;

        s.PerformanceWidgetEnabled = true;
        s.PerformancePosition = WidgetPosition.BottomCenter;

        if (ToggleWidgetsEnabled != null) ToggleWidgetsEnabled.IsOn = true;
        if (ToggleClockWidget != null) ToggleClockWidget.IsOn = true;
        if (ToggleWeatherWidget != null) ToggleWeatherWidget.IsOn = true;
        if (ToggleCalendarWidget != null) ToggleCalendarWidget.IsOn = true;
        if (ToggleQuoteWidget != null) ToggleQuoteWidget.IsOn = true;
        if (ToggleMediaWidget != null) ToggleMediaWidget.IsOn = true;
        if (TogglePerformanceWidget != null) TogglePerformanceWidget.IsOn = true;

        _settingsService.Save();
        UpdateControlStates();
        UpdateAllWidgetPositionPreviews();
    }

    private void AssignWidgetToPosition(string widget, WidgetPosition pos)
    {
        if (_settingsService?.Current == null) return;
        var s = _settingsService.Current;

        switch (widget.ToLowerInvariant())
        {
            case "clock":
                s.ClockPosition = pos;
                s.ClockWidgetEnabled = true;
                if (ToggleClockWidget != null) ToggleClockWidget.IsOn = true;
                break;
            case "weather":
                s.WeatherPosition = pos;
                s.WeatherWidgetEnabled = true;
                if (ToggleWeatherWidget != null) ToggleWeatherWidget.IsOn = true;
                break;
            case "media":
                s.MediaPosition = pos;
                s.MediaWidgetEnabled = true;
                if (ToggleMediaWidget != null) ToggleMediaWidget.IsOn = true;
                break;
            case "performance":
            case "perf":
                s.PerformancePosition = pos;
                s.PerformanceWidgetEnabled = true;
                if (TogglePerformanceWidget != null) TogglePerformanceWidget.IsOn = true;
                break;
            case "calendar":
            case "cal":
                s.CalendarPosition = pos;
                s.CalendarWidgetEnabled = true;
                if (ToggleCalendarWidget != null) ToggleCalendarWidget.IsOn = true;
                break;
            case "quote":
            case "quotes":
                s.QuotePosition = pos;
                s.QuoteWidgetEnabled = true;
                if (ToggleQuoteWidget != null) ToggleQuoteWidget.IsOn = true;
                break;
        }

        _settingsService.Save();
        UpdateControlStates();
        UpdateAllWidgetPositionPreviews();
    }

    private void OnWidgetPositionClicked(object sender, RoutedEventArgs e)
    {
        // Do NOT move or change widget positions on zone click.
        // Positions are only changed when the user drags a badge or selects an action from the Flyout menu.
    }

    private void OnWidgetBadgeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tag && _settingsService?.Current != null)
        {
            var s = _settingsService.Current;
            switch (tag.ToLowerInvariant())
            {
                case "clock":
                    s.ClockWidgetEnabled = !s.ClockWidgetEnabled;
                    if (ToggleClockWidget != null) ToggleClockWidget.IsOn = s.ClockWidgetEnabled;
                    break;
                case "weather":
                    s.WeatherWidgetEnabled = !s.WeatherWidgetEnabled;
                    if (ToggleWeatherWidget != null) ToggleWeatherWidget.IsOn = s.WeatherWidgetEnabled;
                    break;
                case "media":
                    s.MediaWidgetEnabled = !s.MediaWidgetEnabled;
                    if (ToggleMediaWidget != null) ToggleMediaWidget.IsOn = s.MediaWidgetEnabled;
                    break;
                case "performance":
                case "perf":
                    s.PerformanceWidgetEnabled = !s.PerformanceWidgetEnabled;
                    if (TogglePerformanceWidget != null) TogglePerformanceWidget.IsOn = s.PerformanceWidgetEnabled;
                    break;
                case "calendar":
                case "cal":
                    s.CalendarWidgetEnabled = !s.CalendarWidgetEnabled;
                    if (ToggleCalendarWidget != null) ToggleCalendarWidget.IsOn = s.CalendarWidgetEnabled;
                    break;
                case "quote":
                case "quotes":
                    s.QuoteWidgetEnabled = !s.QuoteWidgetEnabled;
                    if (ToggleQuoteWidget != null) ToggleQuoteWidget.IsOn = s.QuoteWidgetEnabled;
                    break;
            }
            _settingsService.Save();
            UpdateControlStates();
            UpdateAllWidgetPositionPreviews();
        }
    }

    private void OnWidgetBadgePreviewClicked(object sender, RoutedEventArgs e)
    {
    }

    private void OnWidgetBadgeDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        string badgeName = "Clock";
        if (sender is FrameworkElement fe && fe.Tag is string tag)
        {
            badgeName = tag;
        }
        args.Data.SetText(badgeName);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnMonitorZoneDragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = DataPackageOperation.Move;
        args.DragUIOverride.Caption = "Place Widget Here";
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;

        if (sender is Button btn)
        {
            btn.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            btn.BorderThickness = new Thickness(2);
        }
    }

    private void OnMonitorZoneDragLeave(object sender, DragEventArgs args)
    {
        UpdateAllWidgetPositionPreviews();
    }

    private async void OnMonitorZoneDrop(object sender, DragEventArgs args)
    {
        if (sender is Button btn && btn.Tag is string tag && Enum.TryParse<WidgetPosition>(tag, out var pos))
        {
            if (args.DataView.Contains(StandardDataFormats.Text))
            {
                var text = await args.DataView.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    AssignWidgetToPosition(text, pos);
                }
            }
        }
    }

    private void OnMonitorZoneKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Delete)
        {
            if (sender is Button btn && btn.Tag is string tag && Enum.TryParse<WidgetPosition>(tag, out var pos))
            {
                ClearWidgetsAtZone(pos);
                e.Handled = true;
            }
        }
    }

    private void OnFlyoutPlaceWidgetClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string tag)
        {
            var parts = tag.Split('|');
            if (parts.Length == 2 && Enum.TryParse<WidgetPosition>(parts[1], out var pos))
            {
                AssignWidgetToPosition(parts[0], pos);
            }
        }
    }

    private void OnFlyoutPlaceClockClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string tag && Enum.TryParse<WidgetPosition>(tag, out var pos))
        {
            AssignWidgetToPosition("Clock", pos);
        }
    }

    private void OnFlyoutPlacePreviewClicked(object sender, RoutedEventArgs e)
    {
    }

    private void ClearWidgetsAtZone(WidgetPosition pos)
    {
        if (_settingsService?.Current == null) return;
        var s = _settingsService.Current;
        if (s.ClockPosition == pos) { s.ClockWidgetEnabled = false; if (ToggleClockWidget != null) ToggleClockWidget.IsOn = false; }
        if (s.WeatherPosition == pos) { s.WeatherWidgetEnabled = false; if (ToggleWeatherWidget != null) ToggleWeatherWidget.IsOn = false; }
        if (s.MediaPosition == pos) { s.MediaWidgetEnabled = false; if (ToggleMediaWidget != null) ToggleMediaWidget.IsOn = false; }
        if (s.PerformancePosition == pos) { s.PerformanceWidgetEnabled = false; if (TogglePerformanceWidget != null) TogglePerformanceWidget.IsOn = false; }
        if (s.CalendarPosition == pos) { s.CalendarWidgetEnabled = false; if (ToggleCalendarWidget != null) ToggleCalendarWidget.IsOn = false; }
        if (s.QuotePosition == pos) { s.QuoteWidgetEnabled = false; if (ToggleQuoteWidget != null) ToggleQuoteWidget.IsOn = false; }

        _settingsService.Save();
        UpdateControlStates();
        UpdateAllWidgetPositionPreviews();
    }

    private void OnFlyoutClearZoneClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string tag && Enum.TryParse<WidgetPosition>(tag, out var pos))
        {
            ClearWidgetsAtZone(pos);
        }
    }

    private void OnRemoveWidgetPlacementClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService?.Current != null)
        {
            var s = _settingsService.Current;
            s.ClockWidgetEnabled = false;
            s.WeatherWidgetEnabled = false;
            s.MediaWidgetEnabled = false;
            s.PerformanceWidgetEnabled = false;
            s.CalendarWidgetEnabled = false;
            s.QuoteWidgetEnabled = false;
            _settingsService.Save();
        }
        if (ToggleClockWidget != null) ToggleClockWidget.IsOn = false;
        if (ToggleWeatherWidget != null) ToggleWeatherWidget.IsOn = false;
        if (ToggleMediaWidget != null) ToggleMediaWidget.IsOn = false;
        if (TogglePerformanceWidget != null) TogglePerformanceWidget.IsOn = false;
        if (ToggleCalendarWidget != null) ToggleCalendarWidget.IsOn = false;
        if (ToggleQuoteWidget != null) ToggleQuoteWidget.IsOn = false;

        UpdateControlStates();
        UpdateAllWidgetPositionPreviews();
    }

    private void OnPlacedClockWidgetTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextFlyout != null)
        {
            fe.ContextFlyout.ShowAt(fe);
            e.Handled = true;
        }
    }

    private void OnMenuMoveClockClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string tag && Enum.TryParse<WidgetPosition>(tag, out var pos))
        {
            AssignWidgetToPosition("Clock", pos);
        }
    }

    private void OnMenuOpenClockSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (ExpanderClockWidget != null)
        {
            ExpanderClockWidget.IsExpanded = true;
            ExpanderClockWidget.StartBringIntoView();
        }
    }

    private void OnMenuRemoveClockClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService?.Current != null)
        {
            _settingsService.Current.ClockWidgetEnabled = false;
            _settingsService.Save();
        }
        if (ToggleClockWidget != null) ToggleClockWidget.IsOn = false;
        UpdateControlStates();
        UpdateAllWidgetPositionPreviews();
    }

    private static (int row, int col) GetPositionRowCol(WidgetPosition pos) => pos switch
    {
        WidgetPosition.TopLeft => (0, 0),
        WidgetPosition.TopCenter => (0, 1),
        WidgetPosition.TopRight => (0, 2),
        WidgetPosition.CenterLeft => (1, 0),
        WidgetPosition.Center => (1, 1),
        WidgetPosition.CenterRight => (1, 2),
        WidgetPosition.BottomLeft => (2, 0),
        WidgetPosition.BottomCenter => (2, 1),
        WidgetPosition.BottomRight => (2, 2),
        _ => (0, 1)
    };

    private void UpdateAllWidgetPositionPreviews()
    {
        var s = _settingsService?.Current;
        if (s == null) return;

        bool widgetsGlobalOn = ToggleWidgetsEnabled?.IsOn ?? s.WidgetsEnabled;
        bool clockOn = widgetsGlobalOn && (ToggleClockWidget?.IsOn ?? s.ClockWidgetEnabled);
        bool weatherOn = widgetsGlobalOn && (ToggleWeatherWidget?.IsOn ?? s.WeatherWidgetEnabled);
        bool mediaOn = widgetsGlobalOn && (ToggleMediaWidget?.IsOn ?? s.MediaWidgetEnabled);
        bool perfOn = widgetsGlobalOn && (TogglePerformanceWidget?.IsOn ?? s.PerformanceWidgetEnabled);
        bool calOn = widgetsGlobalOn && (ToggleCalendarWidget?.IsOn ?? s.CalendarWidgetEnabled);
        bool quoteOn = widgetsGlobalOn && (ToggleQuoteWidget?.IsOn ?? s.QuoteWidgetEnabled);

        // Clock Mini Preview
        if (MiniPreviewClockWidget != null)
        {
            var (row, col) = GetPositionRowCol(s.ClockPosition);
            Grid.SetRow(MiniPreviewClockWidget, row);
            Grid.SetColumn(MiniPreviewClockWidget, col);
            MiniPreviewClockWidget.ClockFontFamily = s.ClockFontFamily ?? "Segoe UI Variable Display";
            MiniPreviewClockWidget.ClockColorPreset = s.ClockColorPreset ?? "Crisp White";
            MiniPreviewClockWidget.DatePosition = s.DatePosition;
            MiniPreviewClockWidget.DateFormat = s.DateFormat;
            MiniPreviewClockWidget.Use24HourFormat = !s.ClockUse12Hour;
            MiniPreviewClockWidget.ShowSeconds = s.ClockShowSeconds;
            MiniPreviewClockWidget.ShowAmPm = s.ClockShowAmPm;
            MiniPreviewClockWidget.ShowDate = s.ClockShowDate;
            MiniPreviewClockWidget.ShowGlassPill = s.ShowGlassPill;
            MiniPreviewClockWidget.GlassPillOpacity = s.GlassPillOpacity;
            MiniPreviewClockWidget.ClockScale = 0.26 * s.ClockScale;
            MiniPreviewClockWidget.Visibility = clockOn ? Visibility.Visible : Visibility.Collapsed;
        }

        // Weather Mini Preview
        if (MiniPreviewWeatherWidget != null)
        {
            MiniPreviewWeatherWidget.WeatherScale = 0.25 * s.WeatherScale;
            MiniPreviewWeatherWidget.CityName = s.WeatherCity ?? "Auto";
            MiniPreviewWeatherWidget.UseFahrenheit = s.WeatherUseFahrenheit;
            MiniPreviewWeatherWidget.UseSystemAccent = s.WidgetUseSystemAccent;
            PlaceMiniPreviewInZone(MiniPreviewWeatherWidget, s.WeatherPosition);
            MiniPreviewWeatherWidget.Visibility = weatherOn ? Visibility.Visible : Visibility.Collapsed;
        }

        // Calendar Mini Preview
        if (MiniPreviewCalendarWidget != null)
        {
            MiniPreviewCalendarWidget.CalendarScale = 0.25 * s.CalendarScale;
            MiniPreviewCalendarWidget.UseSystemAccent = s.WidgetUseSystemAccent;
            MiniPreviewCalendarWidget.ShowWeekNumber = s.CalendarShowWeekNumber;
            MiniPreviewCalendarWidget.ShowEventNote = s.CalendarShowEventNote;
            MiniPreviewCalendarWidget.CustomNote = s.CalendarCustomNote ?? "No upcoming calendar conflicts";
            MiniPreviewCalendarWidget.DateFormatStyle = s.CalendarDateFormat;
            PlaceMiniPreviewInZone(MiniPreviewCalendarWidget, s.CalendarPosition);
            MiniPreviewCalendarWidget.Visibility = calOn ? Visibility.Visible : Visibility.Collapsed;
        }

        // Wisdom & Quotes Live Design Preview Card (inside ExpanderQuoteWidget)
        if (LivePreviewQuoteCard != null)
        {
            LivePreviewQuoteCard.CardWidth = s.QuoteCardWidth;
            LivePreviewQuoteCard.QuoteScale = Math.Clamp(s.QuoteScale, 0.65, 1.25);
            LivePreviewQuoteCard.BackgroundOpacity = s.QuoteBackgroundOpacity;
            LivePreviewQuoteCard.ShowBorder = s.QuoteShowBorder;
            LivePreviewQuoteCard.UseBlurEffect = s.QuoteBlurEffect;
            LivePreviewQuoteCard.BlurIntensity = s.QuoteBlurIntensity;
            LivePreviewQuoteCard.BackdropTheme = s.QuoteBackdropTheme ?? "Acrylic";
            LivePreviewQuoteCard.QuoteAlignment = s.QuoteTextAlignment;
            LivePreviewQuoteCard.QuoteFontStyle = s.QuoteFontStyle ?? "Literary Serif";
            LivePreviewQuoteCard.MainFontSize = s.QuoteMainFontSize;
            LivePreviewQuoteCard.MeaningFontSize = s.QuoteMeaningFontSize;
            LivePreviewQuoteCard.ColorTheme = s.QuoteColorTheme ?? "Warm Cream & Muted Gold";
            LivePreviewQuoteCard.UseSystemAccent = s.WidgetUseSystemAccent;
            LivePreviewQuoteCard.ShowCategoryHeader = s.QuoteShowCategoryHeader;
            LivePreviewQuoteCard.ShowAuthor = s.QuoteShowAuthor;
            LivePreviewQuoteCard.ShowMeaning = s.QuoteShowMeaning;
            LivePreviewQuoteCard.DatabasePath = s.QuoteDatabasePath ?? string.Empty;
            LivePreviewQuoteCard.RotationIntervalSeconds = s.QuoteRotationIntervalSeconds;
        }

        // Wisdom & Quotes Mini Preview (Independent Literary Overlay — Grid cell placement like Clock)
        if (MiniPreviewQuoteWidget != null)
        {
            var (qRow, qCol) = GetPositionRowCol(s.QuotePosition);
            Grid.SetRow(MiniPreviewQuoteWidget, qRow);
            Grid.SetColumn(MiniPreviewQuoteWidget, qCol);
            MiniPreviewQuoteWidget.CardWidth = s.QuoteCardWidth;
            MiniPreviewQuoteWidget.QuoteScale = 0.25 * s.QuoteScale;
            MiniPreviewQuoteWidget.BackgroundOpacity = s.QuoteBackgroundOpacity;
            MiniPreviewQuoteWidget.ShowBorder = s.QuoteShowBorder;
            MiniPreviewQuoteWidget.UseBlurEffect = s.QuoteBlurEffect;
            MiniPreviewQuoteWidget.BlurIntensity = s.QuoteBlurIntensity;
            MiniPreviewQuoteWidget.BackdropTheme = s.QuoteBackdropTheme ?? "Acrylic";
            MiniPreviewQuoteWidget.QuoteAlignment = s.QuoteTextAlignment;
            MiniPreviewQuoteWidget.QuoteFontStyle = s.QuoteFontStyle ?? "Literary Serif";
            MiniPreviewQuoteWidget.MainFontSize = s.QuoteMainFontSize;
            MiniPreviewQuoteWidget.MeaningFontSize = s.QuoteMeaningFontSize;
            MiniPreviewQuoteWidget.ColorTheme = s.QuoteColorTheme ?? "Warm Cream & Muted Gold";
            MiniPreviewQuoteWidget.UseSystemAccent = s.WidgetUseSystemAccent;
            MiniPreviewQuoteWidget.ShowCategoryHeader = s.QuoteShowCategoryHeader;
            MiniPreviewQuoteWidget.ShowAuthor = s.QuoteShowAuthor;
            MiniPreviewQuoteWidget.ShowMeaning = s.QuoteShowMeaning;
            MiniPreviewQuoteWidget.DatabasePath = s.QuoteDatabasePath ?? string.Empty;
            MiniPreviewQuoteWidget.RotationIntervalSeconds = s.QuoteRotationIntervalSeconds;
            MiniPreviewQuoteWidget.Visibility = quoteOn ? Visibility.Visible : Visibility.Collapsed;
        }

        // Media Mini Preview
        if (MiniPreviewMediaWidget != null)
        {
            MiniPreviewMediaWidget.IsWidgetEnabled = mediaOn;
            MiniPreviewMediaWidget.MediaScale = 0.25 * s.MediaScale;
            MiniPreviewMediaWidget.UseSystemAccent = s.WidgetUseSystemAccent;
            MiniPreviewMediaWidget.ShowAlbumArt = s.MediaShowAlbumArt;
            MiniPreviewMediaWidget.HideWhenIdle = s.MediaHideWhenIdle;
            MiniPreviewMediaWidget.IdleText = s.MediaIdleText ?? "Ready for Music";
            PlaceMiniPreviewInZone(MiniPreviewMediaWidget, s.MediaPosition);
            MiniPreviewMediaWidget.ApplyVisibilityState();
        }

        // Performance Mini Preview
        if (MiniPreviewPerformanceWidget != null)
        {
            MiniPreviewPerformanceWidget.PerformanceScale = 0.25 * s.PerformanceScale;
            MiniPreviewPerformanceWidget.UseSystemAccent = s.WidgetUseSystemAccent;
            MiniPreviewPerformanceWidget.ShowCpu = s.PerformanceShowCpu;
            MiniPreviewPerformanceWidget.ShowRam = s.PerformanceShowRam;
            MiniPreviewPerformanceWidget.ShowPercentages = s.PerformanceShowPercentages;
            MiniPreviewPerformanceWidget.HighlightThreshold = s.PerformanceHighlightThreshold;
            PlaceMiniPreviewInZone(MiniPreviewPerformanceWidget, s.PerformancePosition);
            MiniPreviewPerformanceWidget.Visibility = perfOn ? Visibility.Visible : Visibility.Collapsed;
        }

        (Button? btn, Border? dot, WidgetPosition pos)[] zones = [
            (BtnPosTopLeft, DotPosTopLeft, WidgetPosition.TopLeft),
            (BtnPosTopCenter, DotPosTopCenter, WidgetPosition.TopCenter),
            (BtnPosTopRight, DotPosTopRight, WidgetPosition.TopRight),
            (BtnPosCenterLeft, DotPosCenterLeft, WidgetPosition.CenterLeft),
            (BtnPosCenter, DotPosCenter, WidgetPosition.Center),
            (BtnPosCenterRight, DotPosCenterRight, WidgetPosition.CenterRight),
            (BtnPosBottomLeft, DotPosBottomLeft, WidgetPosition.BottomLeft),
            (BtnPosBottomCenter, DotPosBottomCenter, WidgetPosition.BottomCenter),
            (BtnPosBottomRight, DotPosBottomRight, WidgetPosition.BottomRight)
        ];

        var transparentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var defaultBorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
        var defaultBackgroundBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));

        foreach (var (btn, dot, zonePos) in zones)
        {
            if (btn == null) continue;

            bool isZoneOccupied = (clockOn && s.ClockPosition == zonePos)
                               || (weatherOn && s.WeatherPosition == zonePos)
                               || (mediaOn && s.MediaPosition == zonePos)
                               || (perfOn && s.PerformancePosition == zonePos)
                               || (calOn && s.CalendarPosition == zonePos)
                               || (quoteOn && s.QuotePosition == zonePos);

            if (dot != null) dot.Visibility = isZoneOccupied ? Visibility.Collapsed : Visibility.Visible;

            if (isZoneOccupied)
            {
                btn.BorderBrush = transparentBrush;
                btn.BorderThickness = new Thickness(0);
                btn.Background = transparentBrush;
            }
            else
            {
                btn.BorderBrush = defaultBorderBrush;
                btn.BorderThickness = new Thickness(1);
                btn.Background = defaultBackgroundBrush;
            }
        }

        bool anyWidgetPlaced = clockOn || weatherOn || mediaOn || perfOn || calOn || quoteOn;
        if (PanelEmptyPlacementState != null)
        {
            PanelEmptyPlacementState.Visibility = anyWidgetPlaced ? Visibility.Collapsed : Visibility.Visible;
        }
        if (BtnRemovePlacement != null)
        {
            BtnRemovePlacement.IsEnabled = anyWidgetPlaced;
        }

        if (TxtStatusPlacement != null)
        {
            var activeItems = new List<string>();
            if (clockOn) activeItems.Add("Clock");
            if (weatherOn) activeItems.Add("Weather");
            if (mediaOn) activeItems.Add("Media");
            if (perfOn) activeItems.Add("Perf");
            if (calOn) activeItems.Add("Calendar");
            if (quoteOn) activeItems.Add("Quotes");
            TxtStatusPlacement.Text = anyWidgetPlaced ? $"{string.Join(", ", activeItems)} Active" : "No Widgets Placed";
        }
    }

    private void PlaceMiniPreviewInZone(FrameworkElement card, WidgetPosition pos)
    {
        var targetStack = pos switch
        {
            WidgetPosition.TopLeft => MiniZoneStackTopLeft,
            WidgetPosition.TopCenter => MiniZoneStackTopCenter,
            WidgetPosition.TopRight => MiniZoneStackTopRight,
            WidgetPosition.CenterLeft => MiniZoneStackCenterLeft,
            WidgetPosition.Center => MiniZoneStackCenter,
            WidgetPosition.CenterRight => MiniZoneStackCenterRight,
            WidgetPosition.BottomLeft => MiniZoneStackBottomLeft,
            WidgetPosition.BottomCenter => MiniZoneStackBottomCenter,
            WidgetPosition.BottomRight => MiniZoneStackBottomRight,
            _ => MiniZoneStackBottomCenter
        };

        if (targetStack == null) return;

        StackPanel?[] allStacks =
        [
            MiniZoneStackTopLeft, MiniZoneStackTopCenter, MiniZoneStackTopRight,
            MiniZoneStackCenterLeft, MiniZoneStackCenter, MiniZoneStackCenterRight,
            MiniZoneStackBottomLeft, MiniZoneStackBottomCenter, MiniZoneStackBottomRight
        ];

        foreach (var stack in allStacks)
        {
            if (stack != null && stack != targetStack && stack.Children.Contains(card))
            {
                stack.Children.Remove(card);
            }
        }

        if (card.Parent is Panel currentParent && currentParent != targetStack && currentParent.Children.Contains(card))
        {
            currentParent.Children.Remove(card);
        }

        if (!targetStack.Children.Contains(card))
        {
            targetStack.Children.Add(card);
        }
    }

    private void UpdateWidgetPositionHighlight(WidgetPosition pos)
    {
        UpdateAllWidgetPositionPreviews();
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

    public void UpdateMiniMonitorWallpaper()
    {
        if (ImgMiniMonitorWallpaper == null || _mediaLibrary == null || _settingsService == null) return;
        try
        {
            var s = _settingsService.Current;
            var activeTheme = _mediaLibrary.GetActiveTheme();
            string? imagePath = null;

            if (activeTheme != null)
            {
                var (_, _, item) = SolarCalculator.GetCurrentSolarMedia(activeTheme, DateTime.Now, s);
                imagePath = item?.FilePath;
            }

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                var activeItems = _mediaLibrary.GetActiveItems();
                var firstMedia = activeItems.FirstOrDefault(m => m != null && m.Type == MediaType.Image && !string.IsNullOrEmpty(m.FilePath) && File.Exists(m.FilePath));
                imagePath = firstMedia?.FilePath;
            }

            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                var bmp = new BitmapImage
                {
                    DecodePixelWidth = 640,
                    DecodePixelType = DecodePixelType.Logical,
                    UriSource = new Uri(imagePath)
                };
                ImgMiniMonitorWallpaper.Source = bmp;
                ImgMiniMonitorWallpaper.Visibility = Visibility.Visible;
                if (ImgQuoteLivePreviewBg != null)
                {
                    ImgQuoteLivePreviewBg.Source = bmp;
                    ImgQuoteLivePreviewBg.Visibility = Visibility.Visible;
                }
            }
            else
            {
                ImgMiniMonitorWallpaper.Visibility = Visibility.Collapsed;
                if (ImgQuoteLivePreviewBg != null) ImgQuoteLivePreviewBg.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            if (ImgMiniMonitorWallpaper != null) ImgMiniMonitorWallpaper.Visibility = Visibility.Collapsed;
            if (ImgQuoteLivePreviewBg != null) ImgQuoteLivePreviewBg.Visibility = Visibility.Collapsed;
        }
    }

    private void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _settingsService == null) return;
        var enable = ToggleAutoStart.IsOn;
        AutoStartService.SetAutoStart(enable);
        _settingsService.Current.StartWithWindows = enable;
        _settingsService.Save();
    }

    private void OnAddFolderClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null || _mediaLibrary == null) return;
        var path = NativeMethods.PickFolder(_windowHandle, "Select Media or Dynamic Theme Folder");
        if (!string.IsNullOrEmpty(path))
        {
            if (!_settingsService.Current.MediaFolders.Contains(path))
            {
                _settingsService.Current.MediaFolders.Add(path);
                _settingsService.Current.ActiveSourcePath = path;
                var theme = DynamicThemeService.TryLoadThemeFromFolder(path);
                if (theme != null)
                {
                    _settingsService.Current.DynamicThemeEnabled = true;
                    _settingsService.Current.SelectedThemeId = theme.Id;
                }
                else
                {
                    _settingsService.Current.DynamicThemeEnabled = false;
                }
                _settingsService.Save();
                UpdateSourceList();
                _mediaLibrary.Rescan();
            }
        }
    }

    private void OnRescanClicked(object sender, RoutedEventArgs e)
    {
        _mediaLibrary?.Rescan();
    }

    private void OnPreviewNowClicked(object sender, RoutedEventArgs e)
    {
        ScreensaverRequested?.Invoke();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            if (PanelGeneral != null) PanelGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
            if (PanelDisplays != null) PanelDisplays.Visibility = tag == "displays" ? Visibility.Visible : Visibility.Collapsed;
            if (PanelMedia != null) PanelMedia.Visibility = tag == "media" ? Visibility.Visible : Visibility.Collapsed;
            if (PanelWidgets != null) PanelWidgets.Visibility = tag == "widgets" ? Visibility.Visible : Visibility.Collapsed;
            if (PanelPower != null) PanelPower.Visibility = tag == "power" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void NavView_PaneOpened(NavigationView sender, object args)
    {
        UpdatePaneFooterLayout(isPaneOpen: true);
    }

    private void NavView_PaneClosed(NavigationView sender, object args)
    {
        UpdatePaneFooterLayout(isPaneOpen: false);
    }

    private void UpdatePaneFooterLayout(bool isPaneOpen)
    {
        if (TxtPreviewBtnText != null)
            TxtPreviewBtnText.Visibility = isPaneOpen ? Visibility.Visible : Visibility.Collapsed;

        if (TxtHotkeyHint != null)
            TxtHotkeyHint.Visibility = isPaneOpen ? Visibility.Visible : Visibility.Collapsed;

        if (BtnPreviewNow != null)
        {
            if (isPaneOpen)
            {
                BtnPreviewNow.HorizontalAlignment = HorizontalAlignment.Stretch;
                BtnPreviewNow.Width = double.NaN;
                BtnPreviewNow.Height = double.NaN;
                BtnPreviewNow.Padding = new Thickness(8, 6, 8, 6);
            }
            else
            {
                BtnPreviewNow.HorizontalAlignment = HorizontalAlignment.Center;
                BtnPreviewNow.Width = 36;
                BtnPreviewNow.Height = 36;
                BtnPreviewNow.Padding = new Thickness(0);
            }
        }

        if (PanelPaneFooter != null)
        {
            PanelPaneFooter.Margin = isPaneOpen ? new Thickness(12, 8, 12, 8) : new Thickness(4, 8, 4, 8);
        }
    }
}
