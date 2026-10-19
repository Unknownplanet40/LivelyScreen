using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LivelyScreen_WinUI.Controls;

public sealed partial class StylizedWeatherWidget : UserControl
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static DateTime _lastFetchTime = DateTime.MinValue;
    private static string _cachedTemp = "22°";
    private static string _cachedCondition = "Partly Cloudy";
    private static string _cachedHighLow = "H: 25°  L: 18°";
    private static string _cachedGlyph = "\uE706";
    private static Windows.UI.Color _cachedColor = Windows.UI.Color.FromArgb(255, 245, 200, 76);

    private readonly DispatcherTimer _refreshTimer = new();

    public static readonly DependencyProperty WeatherScaleProperty =
        DependencyProperty.Register(nameof(WeatherScale), typeof(double), typeof(StylizedWeatherWidget), new PropertyMetadata(1.0, OnScaleChanged));

    public static readonly DependencyProperty CityNameProperty =
        DependencyProperty.Register(nameof(CityName), typeof(string), typeof(StylizedWeatherWidget), new PropertyMetadata("Auto", OnConfigChanged));

    public static readonly DependencyProperty UseFahrenheitProperty =
        DependencyProperty.Register(nameof(UseFahrenheit), typeof(bool), typeof(StylizedWeatherWidget), new PropertyMetadata(false, OnConfigChanged));

    public static readonly DependencyProperty UseSystemAccentProperty =
        DependencyProperty.Register(nameof(UseSystemAccent), typeof(bool), typeof(StylizedWeatherWidget), new PropertyMetadata(false, OnAccentModeChanged));

    public double WeatherScale
    {
        get => (double)GetValue(WeatherScaleProperty);
        set => SetValue(WeatherScaleProperty, value);
    }

    public string CityName
    {
        get => (string)GetValue(CityNameProperty);
        set => SetValue(CityNameProperty, value);
    }

    public string City
    {
        get => CityName;
        set => CityName = value;
    }

    public bool UseFahrenheit
    {
        get => (bool)GetValue(UseFahrenheitProperty);
        set => SetValue(UseFahrenheitProperty, value);
    }

    public bool UseSystemAccent
    {
        get => (bool)GetValue(UseSystemAccentProperty);
        set => SetValue(UseSystemAccentProperty, value);
    }

    public StylizedWeatherWidget()
    {
        this.InitializeComponent();
        ApplyScale(WeatherScale);

        _refreshTimer.Interval = TimeSpan.FromMinutes(20);
        _refreshTimer.Tick += (st, et) =>
        {
            if (Visibility == Visibility.Visible)
                _ = FetchWeatherAsync();
        };

        this.Loaded += (s, e) =>
        {
            ApplyCachedData();
            if (Visibility == Visibility.Visible)
                _ = FetchWeatherAsync();
            _refreshTimer.Start();
        };

        this.Unloaded += (s, e) => _refreshTimer.Stop();
    }

    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedWeatherWidget w && e.NewValue is double s)
        {
            w.ApplyScale(s);
        }
    }

    private static void OnConfigChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedWeatherWidget w)
        {
            _ = w.FetchWeatherAsync(force: true);
        }
    }

    private static void OnAccentModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StylizedWeatherWidget w)
        {
            w.ApplyIconPalette();
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

    private void ApplyIconPalette()
    {
        if (IconWeather == null || IconWeatherGlow == null) return;

        if (UseSystemAccent && Application.Current.Resources.TryGetValue("AccentTextFillColorPrimaryBrush", out var accentObj) && accentObj is Brush accentBrush)
        {
            IconWeather.Foreground = accentBrush;
            IconWeatherGlow.Opacity = 0.22;
            IconWeatherGlow.Foreground = accentBrush;
        }
        else
        {
            IconWeather.Foreground = new SolidColorBrush(_cachedColor);
            IconWeatherGlow.Opacity = 1.0;
            IconWeatherGlow.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(48, _cachedColor.R, _cachedColor.G, _cachedColor.B));
        }
    }

    private void ApplyCachedData()
    {
        if (TxtTemperature != null) TxtTemperature.Text = _cachedTemp;
        if (TxtCondition != null) TxtCondition.Text = _cachedCondition;
        if (TxtHighLow != null) TxtHighLow.Text = _cachedHighLow;
        if (TxtUnitLabel != null) TxtUnitLabel.Text = UseFahrenheit ? "Fahrenheit (°F)" : "Celsius (°C)";
        if (IconWeather != null) IconWeather.Glyph = _cachedGlyph;
        if (IconWeatherGlow != null) IconWeatherGlow.Glyph = _cachedGlyph;
        if (TxtLocation != null) TxtLocation.Text = string.IsNullOrWhiteSpace(CityName) || CityName.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? "Local Weather" : CityName;
        ApplyIconPalette();
    }

    public async Task FetchWeatherAsync(bool force = false)
    {
        if (!force && DateTime.UtcNow - _lastFetchTime < TimeSpan.FromMinutes(20))
        {
            ApplyCachedData();
            return;
        }

        try
        {
            double lat = 37.7749, lon = -122.4194;
            string tempUnit = UseFahrenheit ? "fahrenheit" : "celsius";

            string displayCity = "Local Weather";
            if (!string.IsNullOrWhiteSpace(CityName) && !CityName.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                displayCity = CityName;
                try
                {
                    string geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(CityName)}&count=1&language=en&format=json";
                    var geoJson = await _httpClient.GetStringAsync(geoUrl);
                    using var geoDoc = JsonDocument.Parse(geoJson);
                    if (geoDoc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                    {
                        var first = results[0];
                        lat = first.GetProperty("latitude").GetDouble();
                        lon = first.GetProperty("longitude").GetDouble();
                    }
                }
                catch { }
            }

            string weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat:F4}&longitude={lon:F4}&current=temperature_2m,weather_code&daily=temperature_2m_max,temperature_2m_min&temperature_unit={tempUnit}&timezone=auto";
            var json = await _httpClient.GetStringAsync(weatherUrl);
            using var doc = JsonDocument.Parse(json);

            var current = doc.RootElement.GetProperty("current");
            double temp = current.GetProperty("temperature_2m").GetDouble();
            int code = current.GetProperty("weather_code").GetInt32();

            var daily = doc.RootElement.GetProperty("daily");
            double maxTemp = daily.GetProperty("temperature_2m_max")[0].GetDouble();
            double minTemp = daily.GetProperty("temperature_2m_min")[0].GetDouble();

            var (condText, glyph, color) = MapWeatherCode(code);

            _cachedTemp = $"{Math.Round(temp)}°";
            _cachedCondition = condText;
            _cachedHighLow = $"H: {Math.Round(maxTemp)}°  L: {Math.Round(minTemp)}°";
            _cachedGlyph = glyph;
            _cachedColor = color;
            _lastFetchTime = DateTime.UtcNow;

            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyCachedData();
                if (TxtLocation != null) TxtLocation.Text = displayCity;
            });
        }
        catch
        {
            DispatcherQueue.TryEnqueue(ApplyCachedData);
        }
    }

    private static (string text, string glyph, Windows.UI.Color color) MapWeatherCode(int code)
    {
        return code switch
        {
            0 => ("Clear Sky", "\uE706", Windows.UI.Color.FromArgb(255, 245, 200, 76)),
            1 or 2 => ("Partly Cloudy", "\uE753", Windows.UI.Color.FromArgb(255, 245, 200, 76)),
            3 => ("Overcast", "\uE753", Windows.UI.Color.FromArgb(255, 186, 198, 210)),
            45 or 48 => ("Foggy", "\uE753", Windows.UI.Color.FromArgb(255, 162, 176, 190)),
            51 or 53 or 55 => ("Drizzle", "\uE753", Windows.UI.Color.FromArgb(255, 96, 205, 255)),
            61 or 63 or 65 => ("Rain", "\uE753", Windows.UI.Color.FromArgb(255, 96, 205, 255)),
            71 or 73 or 75 => ("Snow", "\uE753", Windows.UI.Color.FromArgb(255, 220, 238, 250)),
            80 or 81 or 82 => ("Showers", "\uE753", Windows.UI.Color.FromArgb(255, 96, 205, 255)),
            95 or 96 or 99 => ("Thunderstorm", "\uE753", Windows.UI.Color.FromArgb(255, 245, 184, 65)),
            _ => ("Partly Cloudy", "\uE753", Windows.UI.Color.FromArgb(255, 245, 200, 76))
        };
    }
}
