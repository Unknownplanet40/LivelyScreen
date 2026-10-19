using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LivelyScreen.Services;

namespace LivelyScreen_WinUI.Controls;

public sealed partial class StylizedQuoteWidget : UserControl
{
    private readonly DispatcherTimer _rotationTimer = new();
    private readonly DispatcherTimer _fadeTimer = new();
    private bool _fadingOut = false;
    private double _currentOpacity = 1.0;
    private QuoteItem? _currentItem;
    private QuoteBackdropBlurBrush? _blurBrush;
    private Microsoft.UI.Xaml.Media.AcrylicBrush? _acrylicBrush;
    private Brush? _matteGradientBrush;

    public static readonly DependencyProperty QuoteScaleProperty =
        DependencyProperty.Register(nameof(QuoteScale), typeof(double), typeof(StylizedQuoteWidget), new PropertyMetadata(1.0, OnVisualStyleChanged));

    public static readonly DependencyProperty CardWidthProperty =
        DependencyProperty.Register(nameof(CardWidth), typeof(double), typeof(StylizedQuoteWidget), new PropertyMetadata(340.0, OnVisualStyleChanged));

    public static readonly DependencyProperty BackgroundOpacityProperty =
        DependencyProperty.Register(nameof(BackgroundOpacity), typeof(double), typeof(StylizedQuoteWidget), new PropertyMetadata(0.85, OnVisualStyleChanged));

    public static readonly DependencyProperty ShowBorderProperty =
        DependencyProperty.Register(nameof(ShowBorder), typeof(bool), typeof(StylizedQuoteWidget), new PropertyMetadata(true, OnVisualStyleChanged));

    public static readonly DependencyProperty UseBlurEffectProperty =
        DependencyProperty.Register(nameof(UseBlurEffect), typeof(bool), typeof(StylizedQuoteWidget), new PropertyMetadata(true, OnVisualStyleChanged));

    public static readonly DependencyProperty BlurIntensityProperty =
        DependencyProperty.Register(nameof(BlurIntensity), typeof(double), typeof(StylizedQuoteWidget), new PropertyMetadata(18.0, OnVisualStyleChanged));

    public static readonly DependencyProperty BackdropThemeProperty =
        DependencyProperty.Register(nameof(BackdropTheme), typeof(string), typeof(StylizedQuoteWidget), new PropertyMetadata("Acrylic", OnVisualStyleChanged));

    public static readonly DependencyProperty QuoteAlignmentProperty =
        DependencyProperty.Register(nameof(QuoteAlignment), typeof(int), typeof(StylizedQuoteWidget), new PropertyMetadata(0, OnVisualStyleChanged));

    public static readonly DependencyProperty QuoteFontStyleProperty =
        DependencyProperty.Register(nameof(QuoteFontStyle), typeof(string), typeof(StylizedQuoteWidget), new PropertyMetadata("Literary Serif", OnVisualStyleChanged));

    public static readonly DependencyProperty MainFontSizeProperty =
        DependencyProperty.Register(nameof(MainFontSize), typeof(double), typeof(StylizedQuoteWidget), new PropertyMetadata(16.0, OnVisualStyleChanged));

    public static readonly DependencyProperty MeaningFontSizeProperty =
        DependencyProperty.Register(nameof(MeaningFontSize), typeof(double), typeof(StylizedQuoteWidget), new PropertyMetadata(11.5, OnVisualStyleChanged));

    public static readonly DependencyProperty ColorThemeProperty =
        DependencyProperty.Register(nameof(ColorTheme), typeof(string), typeof(StylizedQuoteWidget), new PropertyMetadata("Warm Cream & Muted Gold", OnVisualStyleChanged));

    public static readonly DependencyProperty UseSystemAccentProperty =
        DependencyProperty.Register(nameof(UseSystemAccent), typeof(bool), typeof(StylizedQuoteWidget), new PropertyMetadata(false, OnVisualStyleChanged));

    public static readonly DependencyProperty ShowCategoryHeaderProperty =
        DependencyProperty.Register(nameof(ShowCategoryHeader), typeof(bool), typeof(StylizedQuoteWidget), new PropertyMetadata(true, OnContentToggleChanged));

    public static readonly DependencyProperty ShowAuthorProperty =
        DependencyProperty.Register(nameof(ShowAuthor), typeof(bool), typeof(StylizedQuoteWidget), new PropertyMetadata(true, OnContentToggleChanged));

    public static readonly DependencyProperty ShowMeaningProperty =
        DependencyProperty.Register(nameof(ShowMeaning), typeof(bool), typeof(StylizedQuoteWidget), new PropertyMetadata(true, OnContentToggleChanged));

    public static readonly DependencyProperty DatabasePathProperty =
        DependencyProperty.Register(nameof(DatabasePath), typeof(string), typeof(StylizedQuoteWidget), new PropertyMetadata(string.Empty, OnDatabasePathChanged));

    public static readonly DependencyProperty RotationIntervalSecondsProperty =
        DependencyProperty.Register(nameof(RotationIntervalSeconds), typeof(int), typeof(StylizedQuoteWidget), new PropertyMetadata(30, OnRotationIntervalChanged));

    public double QuoteScale
    {
        get => (double)GetValue(QuoteScaleProperty);
        set => SetValue(QuoteScaleProperty, value);
    }

    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double BackgroundOpacity
    {
        get => (double)GetValue(BackgroundOpacityProperty);
        set => SetValue(BackgroundOpacityProperty, value);
    }

    public bool ShowBorder
    {
        get => (bool)GetValue(ShowBorderProperty);
        set => SetValue(ShowBorderProperty, value);
    }

    public bool UseBlurEffect
    {
        get => (bool)GetValue(UseBlurEffectProperty);
        set => SetValue(UseBlurEffectProperty, value);
    }

    public double BlurIntensity
    {
        get => (double)GetValue(BlurIntensityProperty);
        set => SetValue(BlurIntensityProperty, value);
    }

    public string BackdropTheme
    {
        get => (string)GetValue(BackdropThemeProperty);
        set => SetValue(BackdropThemeProperty, value);
    }

    public int QuoteAlignment
    {
        get => (int)GetValue(QuoteAlignmentProperty);
        set => SetValue(QuoteAlignmentProperty, value);
    }

    public string QuoteFontStyle
    {
        get => (string)GetValue(QuoteFontStyleProperty);
        set => SetValue(QuoteFontStyleProperty, value);
    }

    public double MainFontSize
    {
        get => (double)GetValue(MainFontSizeProperty);
        set => SetValue(MainFontSizeProperty, value);
    }

    public double MeaningFontSize
    {
        get => (double)GetValue(MeaningFontSizeProperty);
        set => SetValue(MeaningFontSizeProperty, value);
    }

    public string ColorTheme
    {
        get => (string)GetValue(ColorThemeProperty);
        set => SetValue(ColorThemeProperty, value);
    }

    public bool UseSystemAccent
    {
        get => (bool)GetValue(UseSystemAccentProperty);
        set => SetValue(UseSystemAccentProperty, value);
    }

    public bool ShowCategoryHeader
    {
        get => (bool)GetValue(ShowCategoryHeaderProperty);
        set => SetValue(ShowCategoryHeaderProperty, value);
    }

    public bool ShowAuthor
    {
        get => (bool)GetValue(ShowAuthorProperty);
        set => SetValue(ShowAuthorProperty, value);
    }

    public bool ShowMeaning
    {
        get => (bool)GetValue(ShowMeaningProperty);
        set => SetValue(ShowMeaningProperty, value);
    }

    public string DatabasePath
    {
        get => (string)GetValue(DatabasePathProperty);
        set => SetValue(DatabasePathProperty, value);
    }

    public int RotationIntervalSeconds
    {
        get => (int)GetValue(RotationIntervalSecondsProperty);
        set => SetValue(RotationIntervalSecondsProperty, value);
    }

    public StylizedQuoteWidget()
    {
        this.InitializeComponent();
        ApplyVisualStyle();

        _rotationTimer.Interval = TimeSpan.FromSeconds(30);
        _rotationTimer.Tick += (s, e) =>
        {
            if (Visibility == Visibility.Visible && RotationIntervalSeconds > 0)
            {
                BeginSmoothQuoteTransition();
            }
        };

        _fadeTimer.Interval = TimeSpan.FromMilliseconds(16);
        _fadeTimer.Tick += (s, e) => StepFadeTransition();

        this.Loaded += (s, e) =>
        {
            ApplyVisualStyle();
            ShowNextQuoteImmediate(false);
            ConfigureRotationTimer();
        };

        this.Unloaded += (s, e) =>
        {
            _rotationTimer.Stop();
            _fadeTimer.Stop();
        };
    }

    private static void OnVisualStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedQuoteWidget w)
        {
            w.ApplyVisualStyle();
        }
    }

    private static void OnContentToggleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedQuoteWidget w)
        {
            w.ApplyVisualStyle();
            if (w._currentItem != null)
            {
                w.ApplyQuoteToControls(w._currentItem);
            }
        }
    }

    private static void OnDatabasePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedQuoteWidget w)
        {
            w.ShowNextQuoteImmediate(true);
        }
    }

    private static void OnRotationIntervalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedQuoteWidget w)
        {
            w.ConfigureRotationTimer();
        }
    }

    private void ConfigureRotationTimer()
    {
        _rotationTimer.Stop();
        if (RotationIntervalSeconds > 0)
        {
            _rotationTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, RotationIntervalSeconds));
            if (IsLoaded)
            {
                _rotationTimer.Start();
            }
        }
    }

    private void ApplyVisualStyle()
    {
        double w = Math.Clamp(CardWidth, 240.0, 560.0);
        double s = Math.Max(0.1, QuoteScale);

        if (PillBackdrop != null)
        {
            _matteGradientBrush ??= PillBackdrop.Background;
            PillBackdrop.Width = w;
            PillBackdrop.BorderThickness = ShowBorder ? new Thickness(1) : new Thickness(0);
        }

        if (ScaleViewbox != null)
        {
            ScaleViewbox.Width = Math.Round(w * s);
        }

        // 1. Background Opacity, Frosted Blur Effect & Readability Shadow
        double bgOp = Math.Clamp(BackgroundOpacity, 0.0, 1.0);
        byte alphaStart = (byte)Math.Round(bgOp * 255);
        byte alphaEnd = (byte)Math.Round(Math.Max(0.0, bgOp - 0.03) * 255);

        if (BgStopStart != null)
            BgStopStart.Color = Windows.UI.Color.FromArgb(alphaStart, 30, 28, 36);
        if (BgStopEnd != null)
            BgStopEnd.Color = Windows.UI.Color.FromArgb(alphaEnd, 20, 19, 25);

        if (PillBackdrop != null)
        {
            if (UseBlurEffect && bgOp > 0.005)
            {
                string theme = BackdropTheme ?? "Acrylic";
                double blurRadius = Math.Clamp(BlurIntensity, 4.0, 64.0);

                if (string.Equals(theme, "Acrylic", StringComparison.OrdinalIgnoreCase))
                {
                    _acrylicBrush ??= new Microsoft.UI.Xaml.Media.AcrylicBrush();
                    _acrylicBrush.TintColor = Windows.UI.Color.FromArgb(255, 26, 24, 34);
                    _acrylicBrush.TintOpacity = Math.Clamp(bgOp * 0.78, 0.05, 0.90);
                    _acrylicBrush.TintLuminosityOpacity = Math.Clamp(bgOp * 0.65, 0.10, 0.85);
                    _acrylicBrush.FallbackColor = Windows.UI.Color.FromArgb((byte)Math.Round(bgOp * 255), 26, 24, 34);

                    if (!ReferenceEquals(PillBackdrop.Background, _acrylicBrush))
                    {
                        PillBackdrop.Background = _acrylicBrush;
                    }
                }
                else if (string.Equals(theme, "Mica Satin", StringComparison.OrdinalIgnoreCase))
                {
                    double micaBlur = Math.Clamp(blurRadius * 0.45, 4.0, 14.0);
                    byte micaAlpha = (byte)Math.Round(Math.Clamp(bgOp * 0.86, 0.10, 0.94) * 255);
                    var micaTint = Windows.UI.Color.FromArgb(micaAlpha, 20, 22, 28);

                    if (_blurBrush == null)
                    {
                        _blurBrush = new QuoteBackdropBlurBrush(micaBlur, micaTint);
                    }
                    else
                    {
                        _blurBrush.UpdateBlurAmount(micaBlur);
                        _blurBrush.UpdateTintColor(micaTint);
                    }

                    if (!ReferenceEquals(PillBackdrop.Background, _blurBrush))
                    {
                        PillBackdrop.Background = _blurBrush;
                    }
                }
                else // "Frosted Blur"
                {
                    byte blurAlpha = (byte)Math.Round(Math.Clamp(bgOp * 0.78, 0.05, 0.90) * 255);
                    var blurTint = Windows.UI.Color.FromArgb(blurAlpha, 26, 24, 34);

                    if (_blurBrush == null)
                    {
                        _blurBrush = new QuoteBackdropBlurBrush(blurRadius, blurTint);
                    }
                    else
                    {
                        _blurBrush.UpdateBlurAmount(blurRadius);
                        _blurBrush.UpdateTintColor(blurTint);
                    }

                    if (!ReferenceEquals(PillBackdrop.Background, _blurBrush))
                    {
                        PillBackdrop.Background = _blurBrush;
                    }
                }
            }
            else if (_matteGradientBrush != null && !ReferenceEquals(PillBackdrop.Background, _matteGradientBrush))
            {
                PillBackdrop.Background = _matteGradientBrush;
            }
        }

        if (TxtQuoteBodyShadow != null)
        {
            // Stronger shadow when background opacity is low or transparent (floating mode)
            double shadowOpacity = bgOp < 0.45 ? 0.85 : 0.40;
            TxtQuoteBodyShadow.Opacity = shadowOpacity;
        }

        // 2. Typography & Font Sizes
        string fontFamilyStr = (QuoteFontStyle ?? "Literary Serif") switch
        {
            "Modern Display Sans" => "Segoe UI Variable Display, Segoe UI",
            "Classic Book Antiqua" => "Palatino Linotype, Book Antiqua, Georgia",
            "Monospace" => "Cascadia Code, Consolas",
            _ => "Georgia, Sitka Text, Palatino Linotype"
        };
        var quoteFont = new FontFamily(fontFamilyStr);

        double mainPt = Math.Clamp(MainFontSize, 11.0, 32.0);
        double mainLineHeight = Math.Round(mainPt * 1.46, 1);

        if (TxtQuoteBody != null)
        {
            TxtQuoteBody.FontFamily = quoteFont;
            TxtQuoteBody.FontSize = mainPt;
            TxtQuoteBody.LineHeight = mainLineHeight;
        }

        if (TxtQuoteBodyShadow != null)
        {
            TxtQuoteBodyShadow.FontFamily = quoteFont;
            TxtQuoteBodyShadow.FontSize = mainPt;
            TxtQuoteBodyShadow.LineHeight = mainLineHeight;
        }

        double subPt = Math.Clamp(MeaningFontSize, 8.5, 18.0);
        if (TxtMeaningBody != null)
        {
            TxtMeaningBody.FontSize = subPt;
            TxtMeaningBody.LineHeight = Math.Round(subPt * 1.44, 1);
        }
        if (TxtAuthorSource != null)
        {
            TxtAuthorSource.FontSize = subPt;
        }
        if (TxtQuoteCategory != null)
        {
            TxtQuoteCategory.FontSize = Math.Max(8.5, Math.Round(subPt - 2.0, 1));
        }
        if (TxtMeaningHeading != null)
        {
            TxtMeaningHeading.FontSize = Math.Max(8.0, Math.Round(subPt - 2.5, 1));
        }
        if (TxtOrnamentMark != null)
        {
            TxtOrnamentMark.FontSize = Math.Round(mainPt * 1.6, 1);
        }

        // 3. Alignment (0 = Left Editorial, 1 = Centered Poetry)
        bool isCentered = QuoteAlignment == 1;
        var hAlign = isCentered ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        var tAlign = isCentered ? TextAlignment.Center : TextAlignment.Left;

        if (HeaderOrnamentPanel != null)
        {
            HeaderOrnamentPanel.HorizontalAlignment = hAlign;
            HeaderOrnamentPanel.Visibility = ShowCategoryHeader ? Visibility.Visible : Visibility.Collapsed;
        }
        if (TxtQuoteBody != null)
        {
            TxtQuoteBody.HorizontalAlignment = hAlign;
            TxtQuoteBody.TextAlignment = tAlign;
        }
        if (TxtQuoteBodyShadow != null)
        {
            TxtQuoteBodyShadow.HorizontalAlignment = hAlign;
            TxtQuoteBodyShadow.TextAlignment = tAlign;
        }
        if (TxtAuthorSource != null)
        {
            TxtAuthorSource.HorizontalAlignment = hAlign;
            TxtAuthorSource.TextAlignment = tAlign;
        }
        if (TxtMeaningHeading != null)
        {
            TxtMeaningHeading.HorizontalAlignment = hAlign;
            TxtMeaningHeading.TextAlignment = tAlign;
        }
        if (TxtMeaningBody != null)
        {
            TxtMeaningBody.HorizontalAlignment = hAlign;
            TxtMeaningBody.TextAlignment = tAlign;
        }

        // 4. Color Theme Palette
        ApplyColorPalette(isCentered);
    }

    private void ApplyColorPalette(bool isCentered)
    {
        // Primary Quote Text, Accent Ornament/Author, Meaning Body, Heading/Category, Border Tint
        Windows.UI.Color primaryColor;
        Windows.UI.Color accentColor;
        Windows.UI.Color meaningColor;
        Windows.UI.Color labelColor;
        Windows.UI.Color borderColor;

        string theme = ColorTheme ?? "Warm Cream & Muted Gold";
        if (theme == "Windows System Accent" || (UseSystemAccent && theme == "Warm Cream & Muted Gold"))
        {
            primaryColor = Windows.UI.Color.FromArgb(255, 248, 248, 252);
            meaningColor = Windows.UI.Color.FromArgb(255, 174, 178, 190);
            labelColor = Windows.UI.Color.FromArgb(255, 136, 140, 154);
            borderColor = Windows.UI.Color.FromArgb(34, 255, 255, 255);
            accentColor = Windows.UI.Color.FromArgb(255, 96, 205, 255);

            if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accentObj) &&
                accentObj is SolidColorBrush sysBrush)
            {
                accentColor = sysBrush.Color;
            }
        }
        else if (theme == "Crisp White & Silver")
        {
            primaryColor = Windows.UI.Color.FromArgb(255, 250, 251, 255);   // #FAFBFF
            accentColor = Windows.UI.Color.FromArgb(255, 196, 204, 218);    // #C4CCDA
            meaningColor = Windows.UI.Color.FromArgb(255, 168, 174, 186);   // #A8AEBA
            labelColor = Windows.UI.Color.FromArgb(255, 130, 136, 148);     // #828894
            borderColor = Windows.UI.Color.FromArgb(34, 240, 244, 255);
        }
        else if (theme == "Soft Lavender & Moonlight")
        {
            primaryColor = Windows.UI.Color.FromArgb(255, 244, 240, 250);   // #F4F0FA
            accentColor = Windows.UI.Color.FromArgb(255, 188, 170, 216);    // #BCAAD8
            meaningColor = Windows.UI.Color.FromArgb(255, 168, 160, 184);   // #A8A0B8
            labelColor = Windows.UI.Color.FromArgb(255, 136, 128, 152);     // #888098
            borderColor = Windows.UI.Color.FromArgb(34, 224, 210, 248);
        }
        else if (theme == "Sage & Parchment")
        {
            primaryColor = Windows.UI.Color.FromArgb(255, 243, 244, 236);   // #F3F4EC
            accentColor = Windows.UI.Color.FromArgb(255, 158, 188, 162);    // #9EBCA2
            meaningColor = Windows.UI.Color.FromArgb(255, 164, 170, 160);   // #A4AAA0
            labelColor = Windows.UI.Color.FromArgb(255, 130, 138, 126);     // #828A7E
            borderColor = Windows.UI.Color.FromArgb(32, 225, 236, 220);
        }
        else // Warm Cream & Muted Gold (Default)
        {
            primaryColor = Windows.UI.Color.FromArgb(255, 247, 243, 235);   // #F7F3EB
            accentColor = Windows.UI.Color.FromArgb(255, 212, 180, 131);    // #D4B483
            meaningColor = Windows.UI.Color.FromArgb(255, 168, 163, 155);   // #A8A39B
            labelColor = Windows.UI.Color.FromArgb(255, 142, 135, 125);     // #8E877D
            borderColor = Windows.UI.Color.FromArgb(32, 247, 243, 235);
        }

        if (TxtQuoteBody != null)
            TxtQuoteBody.Foreground = new SolidColorBrush(primaryColor);
        if (TxtOrnamentMark != null)
            TxtOrnamentMark.Foreground = new SolidColorBrush(accentColor);
        if (TxtAuthorSource != null)
            TxtAuthorSource.Foreground = new SolidColorBrush(accentColor);
        if (TxtMeaningBody != null)
            TxtMeaningBody.Foreground = new SolidColorBrush(meaningColor);
        if (TxtQuoteCategory != null)
            TxtQuoteCategory.Foreground = new SolidColorBrush(labelColor);
        if (TxtMeaningHeading != null)
            TxtMeaningHeading.Foreground = new SolidColorBrush(labelColor);
        if (PillBackdrop != null)
            PillBackdrop.BorderBrush = new SolidColorBrush(borderColor);

        if (DividerStop0 != null && DividerStop1 != null && DividerStop2 != null)
        {
            if (isCentered)
            {
                DividerStop0.Offset = 0.0;
                DividerStop0.Color = Windows.UI.Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B);
                DividerStop1.Offset = 0.5;
                DividerStop1.Color = Windows.UI.Color.FromArgb(95, accentColor.R, accentColor.G, accentColor.B);
                DividerStop2.Offset = 1.0;
                DividerStop2.Color = Windows.UI.Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B);
            }
            else
            {
                DividerStop0.Offset = 0.0;
                DividerStop0.Color = Windows.UI.Color.FromArgb(95, accentColor.R, accentColor.G, accentColor.B);
                DividerStop1.Offset = 0.65;
                DividerStop1.Color = Windows.UI.Color.FromArgb(26, primaryColor.R, primaryColor.G, primaryColor.B);
                DividerStop2.Offset = 1.0;
                DividerStop2.Color = Windows.UI.Color.FromArgb(0, primaryColor.R, primaryColor.G, primaryColor.B);
            }
        }
    }

    private void BeginSmoothQuoteTransition()
    {
        _fadingOut = true;
        if (!_fadeTimer.IsEnabled)
        {
            _fadeTimer.Start();
        }
    }

    private void StepFadeTransition()
    {
        if (ContentGrid == null)
        {
            _fadeTimer.Stop();
            return;
        }

        if (_fadingOut)
        {
            _currentOpacity -= 0.12;
            if (_currentOpacity <= 0.08)
            {
                _currentOpacity = 0.08;
                ApplyQuoteToControls(QuoteService.GetNextQuote(DatabasePath));
                _fadingOut = false;
            }
            ContentGrid.Opacity = _currentOpacity;
        }
        else
        {
            _currentOpacity += 0.10;
            if (_currentOpacity >= 1.0)
            {
                _currentOpacity = 1.0;
                ContentGrid.Opacity = 1.0;
                _fadeTimer.Stop();
                return;
            }
            ContentGrid.Opacity = _currentOpacity;
        }
    }

    public void ShowNextQuoteImmediate(bool forceReload = false)
    {
        try
        {
            if (forceReload)
            {
                QuoteService.EnsureQuotesLoaded(DatabasePath, true);
            }
            var item = QuoteService.GetNextQuote(DatabasePath);
            ApplyQuoteToControls(item);
            if (ContentGrid != null)
            {
                _currentOpacity = 1.0;
                ContentGrid.Opacity = 1.0;
            }
        }
        catch { }
    }

    private void ApplyQuoteToControls(QuoteItem item)
    {
        _currentItem = item;

        if (HeaderOrnamentPanel != null)
        {
            HeaderOrnamentPanel.Visibility = ShowCategoryHeader ? Visibility.Visible : Visibility.Collapsed;
        }

        if (TxtQuoteCategory != null)
        {
            string label = string.IsNullOrWhiteSpace(item.Type) ? "QUOTE" : item.Type.ToUpperInvariant();
            TxtQuoteCategory.Text = label;
        }

        if (TxtQuoteBody != null)
        {
            TxtQuoteBody.Text = item.QuoteText;
        }
        if (TxtQuoteBodyShadow != null)
        {
            TxtQuoteBodyShadow.Text = item.QuoteText;
        }

        // Collapse Author/Source cleanly when empty or disabled so there is zero awkward blank space
        if (TxtAuthorSource != null)
        {
            if (ShowAuthor && !string.IsNullOrWhiteSpace(item.AuthorLine))
            {
                TxtAuthorSource.Text = item.AuthorLine;
                TxtAuthorSource.Visibility = Visibility.Visible;
            }
            else
            {
                TxtAuthorSource.Visibility = Visibility.Collapsed;
            }
        }

        // Optional Tertiary Meaning Section
        if (MeaningContainer != null && TxtMeaningHeading != null && TxtMeaningBody != null)
        {
            if (ShowMeaning && !string.IsNullOrWhiteSpace(item.MeaningText))
            {
                TxtMeaningHeading.Text = string.IsNullOrWhiteSpace(item.MeaningHeading)
                    ? "CONTEXT & MEANING"
                    : item.MeaningHeading.ToUpperInvariant();
                TxtMeaningBody.Text = item.MeaningText;
                MeaningContainer.Visibility = Visibility.Visible;
            }
            else
            {
                MeaningContainer.Visibility = Visibility.Collapsed;
            }
        }
    }
}

internal sealed class QuoteBackdropBlurBrush : XamlCompositionBrushBase
{
    private double _blurAmount;
    private Windows.UI.Color _tintColor;

    public QuoteBackdropBlurBrush(double blurAmount, Windows.UI.Color tintColor)
    {
        _blurAmount = blurAmount;
        _tintColor = tintColor;
        FallbackColor = tintColor;
    }

    public void UpdateBlurAmount(double blurAmount)
    {
        _blurAmount = blurAmount;
        if (CompositionBrush != null)
        {
            try
            {
                CompositionBrush.Properties.InsertScalar("Blur.BlurAmount", (float)blurAmount);
            }
            catch { }
        }
    }

    public void UpdateTintColor(Windows.UI.Color tintColor)
    {
        _tintColor = tintColor;
        FallbackColor = tintColor;
        if (CompositionBrush != null)
        {
            try
            {
                CompositionBrush.Properties.InsertColor("Tint.Color", tintColor);
            }
            catch { }
        }
    }

    protected override void OnConnected()
    {
        if (CompositionBrush != null) return;
        try
        {
            var compositor = CompositionTarget.GetCompositorForCurrentThread();
            var blurEffect = new Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = (float)_blurAmount,
                BorderMode = Microsoft.Graphics.Canvas.Effects.EffectBorderMode.Hard,
                Optimization = Microsoft.Graphics.Canvas.Effects.EffectOptimization.Balanced,
                Source = new Microsoft.UI.Composition.CompositionEffectSourceParameter("Backdrop")
            };
            var tintEffect = new Microsoft.Graphics.Canvas.Effects.ColorSourceEffect
            {
                Name = "Tint",
                Color = _tintColor
            };
            var compositeEffect = new Microsoft.Graphics.Canvas.Effects.CompositeEffect
            {
                Mode = Microsoft.Graphics.Canvas.CanvasComposite.SourceOver
            };
            compositeEffect.Sources.Add(blurEffect);
            compositeEffect.Sources.Add(tintEffect);

            var factory = compositor.CreateEffectFactory(compositeEffect, new[] { "Tint.Color", "Blur.BlurAmount" });
            var brush = factory.CreateBrush();
            brush.SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());
            CompositionBrush = brush;
        }
        catch
        {
            // Fallback to FallbackColor if composition effect fails
        }
    }

    protected override void OnDisconnected()
    {
        if (CompositionBrush != null)
        {
            CompositionBrush.Dispose();
            CompositionBrush = null;
        }
    }
}

