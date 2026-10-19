using System;
using System.Threading.Tasks;
using Windows.Media.Control;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LivelyScreen_WinUI.Controls;

public sealed partial class StylizedMediaWidget : UserControl
{
    private readonly DispatcherTimer _pollTimer = new();
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;

    public static readonly DependencyProperty MediaScaleProperty =
        DependencyProperty.Register(nameof(MediaScale), typeof(double), typeof(StylizedMediaWidget), new PropertyMetadata(1.0, OnScaleChanged));

    public static readonly DependencyProperty UseSystemAccentProperty =
        DependencyProperty.Register(nameof(UseSystemAccent), typeof(bool), typeof(StylizedMediaWidget), new PropertyMetadata(false, OnAccentChanged));

    public static readonly DependencyProperty IsWidgetEnabledProperty =
        DependencyProperty.Register(nameof(IsWidgetEnabled), typeof(bool), typeof(StylizedMediaWidget), new PropertyMetadata(true, OnSettingsChanged));

    public static readonly DependencyProperty ShowAlbumArtProperty =
        DependencyProperty.Register(nameof(ShowAlbumArt), typeof(bool), typeof(StylizedMediaWidget), new PropertyMetadata(true, OnSettingsChanged));

    public static readonly DependencyProperty HideWhenIdleProperty =
        DependencyProperty.Register(nameof(HideWhenIdle), typeof(bool), typeof(StylizedMediaWidget), new PropertyMetadata(false, OnSettingsChanged));

    public static readonly DependencyProperty IdleTextProperty =
        DependencyProperty.Register(nameof(IdleText), typeof(string), typeof(StylizedMediaWidget), new PropertyMetadata("Ready for Music", OnSettingsChanged));

    public static readonly DependencyProperty AudioSpectrumEnabledProperty =
        DependencyProperty.Register(nameof(AudioSpectrumEnabled), typeof(bool), typeof(StylizedMediaWidget), new PropertyMetadata(true, OnSettingsChanged));

    public double MediaScale
    {
        get => (double)GetValue(MediaScaleProperty);
        set => SetValue(MediaScaleProperty, value);
    }

    public bool AudioSpectrumEnabled
    {
        get => (bool)GetValue(AudioSpectrumEnabledProperty);
        set => SetValue(AudioSpectrumEnabledProperty, value);
    }

    public bool UseSystemAccent
    {
        get => (bool)GetValue(UseSystemAccentProperty);
        set => SetValue(UseSystemAccentProperty, value);
    }

    public bool IsWidgetEnabled
    {
        get => (bool)GetValue(IsWidgetEnabledProperty);
        set => SetValue(IsWidgetEnabledProperty, value);
    }

    public bool ShowAlbumArt
    {
        get => (bool)GetValue(ShowAlbumArtProperty);
        set => SetValue(ShowAlbumArtProperty, value);
    }

    public bool HideWhenIdle
    {
        get => (bool)GetValue(HideWhenIdleProperty);
        set => SetValue(HideWhenIdleProperty, value);
    }

    public string IdleText
    {
        get => (string)GetValue(IdleTextProperty);
        set => SetValue(IdleTextProperty, value);
    }

    private string _lastTrackKey = "";
    private bool _hasActiveMedia = false;
    private bool _isMediaPlaying = false;
    private readonly DispatcherTimer _spectrumTimer = new();

    public StylizedMediaWidget()
    {
        this.InitializeComponent();
        ApplyScale(MediaScale);

        _pollTimer.Interval = TimeSpan.FromSeconds(2.5);
        _pollTimer.Tick += async (st, et) =>
        {
            if (IsWidgetEnabled)
                await UpdateMediaInfoAsync();
        };

        _spectrumTimer.Interval = TimeSpan.FromMilliseconds(65);
        _spectrumTimer.Tick += (st, et) => UpdateSpectrum();

        this.Loaded += async (s, e) =>
        {
            ApplyAccentPalette();
            ApplyVisibilityState();
            await InitMediaManagerAsync();
            _pollTimer.Start();
            if (IsWidgetEnabled)
                await UpdateMediaInfoAsync();
        };

        this.Unloaded += (s, e) =>
        {
            _pollTimer.Stop();
            _spectrumTimer.Stop();
        };
    }

    private static void OnSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedMediaWidget w)
        {
            w.ApplyVisibilityState();
            _ = w.UpdateMediaInfoAsync();
        }
    }

    public void ApplyVisibilityState()
    {
        if (!IsWidgetEnabled)
        {
            this.Visibility = Visibility.Collapsed;
            return;
        }

        if (HideWhenIdle && !_hasActiveMedia)
        {
            this.Visibility = Visibility.Collapsed;
        }
        else
        {
            this.Visibility = Visibility.Visible;
            if (PillBackdrop != null) PillBackdrop.Opacity = 1.0;
        }
    }

    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedMediaWidget w && e.NewValue is double s)
        {
            w.ApplyScale(s);
        }
    }

    private static void OnAccentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedMediaWidget w)
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
        if (IconPlaybackStatus == null) return;

        if (UseSystemAccent && Application.Current.Resources.TryGetValue("AccentTextFillColorPrimaryBrush", out var accentObj) && accentObj is Brush accentBrush)
        {
            IconPlaybackStatus.Foreground = accentBrush;
            SetSpectrumBrush(accentBrush);
        }
        else
        {
            var defaultBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 205, 255));
            IconPlaybackStatus.Foreground = defaultBrush;
            SetSpectrumBrush(defaultBrush);
        }
    }

    private void SetSpectrumBrush(Brush brush)
    {
        if (Bar0 != null) Bar0.Background = brush;
        if (Bar1 != null) Bar1.Background = brush;
        if (Bar2 != null) Bar2.Background = brush;
        if (Bar3 != null) Bar3.Background = brush;
        if (Bar4 != null) Bar4.Background = brush;
    }

    private void UpdateSpectrum()
    {
        if (Bar0 == null || Bar1 == null || Bar2 == null || Bar3 == null || Bar4 == null) return;
        var bands = LivelyScreen.Services.AudioSpectrumService.GetSpectrumBands(_isMediaPlaying);
        Bar0.Height = Math.Max(3.0, bands[0] * 14.0);
        Bar1.Height = Math.Max(3.0, bands[1] * 14.0);
        Bar2.Height = Math.Max(3.0, bands[2] * 14.0);
        Bar3.Height = Math.Max(3.0, bands[3] * 14.0);
        Bar4.Height = Math.Max(3.0, bands[4] * 14.0);
    }

    private async Task InitMediaManagerAsync()
    {
        try
        {
            _sessionManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch { }
    }

    private async Task UpdateMediaInfoAsync()
    {
        if (!IsWidgetEnabled)
        {
            this.Visibility = Visibility.Collapsed;
            _spectrumTimer.Stop();
            return;
        }

        try
        {
            _sessionManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            var currentSession = _sessionManager?.GetCurrentSession();
            if (currentSession != null)
            {
                var mediaProps = await currentSession.TryGetMediaPropertiesAsync();
                var playbackInfo = currentSession.GetPlaybackInfo();

                string title = mediaProps?.Title ?? "";
                string artist = mediaProps?.Artist ?? "";
                bool isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                _isMediaPlaying = isPlaying;
                if (AudioSpectrumEnabled && isPlaying)
                {
                    if (PanelSpectrum != null) PanelSpectrum.Visibility = Visibility.Visible;
                    if (!_spectrumTimer.IsEnabled) _spectrumTimer.Start();
                }
                else
                {
                    if (PanelSpectrum != null) PanelSpectrum.Visibility = Visibility.Collapsed;
                    _spectrumTimer.Stop();
                }

                if (!string.IsNullOrWhiteSpace(title) && (!HideWhenIdle || isPlaying))
                {
                    _hasActiveMedia = true;
                    this.Visibility = Visibility.Visible;
                    if (PillBackdrop != null) PillBackdrop.Opacity = 1.0;

                    TxtTitle.Text = title;
                    TxtArtist.Text = string.IsNullOrWhiteSpace(artist) ? "Spotify • Apple Music • Web" : artist;
                    TxtPlaybackStatus.Text = isPlaying ? "Playing" : "Paused";
                    IconPlaybackStatus.Glyph = isPlaying ? "\uE769" : "\uE768";

                    string trackKey = $"{title}|{artist}";
                    if (trackKey != _lastTrackKey)
                    {
                        _lastTrackKey = trackKey;
                        if (ShowAlbumArt && mediaProps?.Thumbnail != null)
                        {
                            using var stream = await mediaProps.Thumbnail.OpenReadAsync();
                            var bitmap = new BitmapImage
                            {
                                DecodePixelWidth = 72,
                                DecodePixelType = DecodePixelType.Logical
                            };
                            await bitmap.SetSourceAsync(stream);
                            ImgAlbumArt.Source = bitmap;
                            ImgAlbumArt.Visibility = Visibility.Visible;
                            BorderIconPlaceholder.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            ImgAlbumArt.Source = null;
                            ImgAlbumArt.Visibility = Visibility.Collapsed;
                            BorderIconPlaceholder.Visibility = Visibility.Visible;
                        }
                    }
                    else if (!ShowAlbumArt)
                    {
                        ImgAlbumArt.Source = null;
                        ImgAlbumArt.Visibility = Visibility.Collapsed;
                        BorderIconPlaceholder.Visibility = Visibility.Visible;
                    }
                    return;
                }
            }

            if (!string.IsNullOrEmpty(LivelyScreen.Services.AmbientAudioService.CurrentTrackName))
            {
                _hasActiveMedia = true;
                _isMediaPlaying = true;
                this.Visibility = Visibility.Visible;
                if (PillBackdrop != null) PillBackdrop.Opacity = 1.0;

                TxtTitle.Text = LivelyScreen.Services.AmbientAudioService.CurrentTrackName;
                TxtArtist.Text = "Custom Lo-Fi Soundscape";
                TxtPlaybackStatus.Text = "Playing";
                IconPlaybackStatus.Glyph = "\uE769";
                ImgAlbumArt.Source = null;
                ImgAlbumArt.Visibility = Visibility.Collapsed;
                BorderIconPlaceholder.Visibility = Visibility.Visible;

                if (AudioSpectrumEnabled)
                {
                    if (PanelSpectrum != null) PanelSpectrum.Visibility = Visibility.Visible;
                    if (!_spectrumTimer.IsEnabled) _spectrumTimer.Start();
                }
                return;
            }

            _hasActiveMedia = false;
            _isMediaPlaying = false;
            _spectrumTimer.Stop();
            if (PanelSpectrum != null) PanelSpectrum.Visibility = Visibility.Collapsed;

            ApplyVisibilityState();

            _lastTrackKey = "";
            TxtTitle.Text = string.IsNullOrWhiteSpace(IdleText) ? "Ready for Music" : IdleText;
            TxtArtist.Text = "Spotify • Apple Music • Web";
            TxtPlaybackStatus.Text = "Idle";
            IconPlaybackStatus.Glyph = "\uE768";
            ImgAlbumArt.Source = null;
            ImgAlbumArt.Visibility = Visibility.Collapsed;
            BorderIconPlaceholder.Visibility = Visibility.Visible;
        }
        catch
        {
            _hasActiveMedia = false;
            _isMediaPlaying = false;
            _spectrumTimer.Stop();
            if (PanelSpectrum != null) PanelSpectrum.Visibility = Visibility.Collapsed;

            ApplyVisibilityState();

            _lastTrackKey = "";
            TxtTitle.Text = string.IsNullOrWhiteSpace(IdleText) ? "Ready for Music" : IdleText;
            TxtArtist.Text = "Spotify • Apple Music • Web";
            TxtPlaybackStatus.Text = "Idle";
            ImgAlbumArt.Source = null;
            ImgAlbumArt.Visibility = Visibility.Collapsed;
            BorderIconPlaceholder.Visibility = Visibility.Visible;
        }
    }
}
