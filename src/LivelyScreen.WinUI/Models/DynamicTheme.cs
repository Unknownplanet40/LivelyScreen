using System.Text.Json.Serialization;

namespace LivelyScreen.Models;

public enum DynamicThemeMode
{
    SolarTimeLocked = 0,     // Real-time solar lock: single image matching current solar progress
    SolarSegmentCycle = 1,   // Cycles only through the current solar period's images (e.g. Night images at night)
    FullDayTimeLapse = 2     // Fast-forwards through Sunrise -> Day -> Sunset -> Night
}

public enum SolarSegment
{
    Sunrise = 0,
    Day = 1,
    Sunset = 2,
    Night = 3
}

public enum SolarSegmentOverride
{
    AutoRealTime = 0,
    ForceSunrise = 1,
    ForceDay = 2,
    ForceSunset = 3,
    ForceNight = 4
}

public class WddThemeJson
{
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("imageFilename")]
    public string? ImageFilename { get; set; }

    [JsonPropertyName("imageCredits")]
    public string? ImageCredits { get; set; }

    [JsonPropertyName("dayHighlight")]
    public int? DayHighlight { get; set; }

    [JsonPropertyName("nightHighlight")]
    public int? NightHighlight { get; set; }

    [JsonPropertyName("sunriseImageList")]
    public int[]? SunriseImageList { get; set; }

    [JsonPropertyName("dayImageList")]
    public int[]? DayImageList { get; set; }

    [JsonPropertyName("sunsetImageList")]
    public int[]? SunsetImageList { get; set; }

    [JsonPropertyName("nightImageList")]
    public int[]? NightImageList { get; set; }
}

public class DynamicTheme
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string ImageFilenamePattern { get; set; } = "*.*";
    public string Credits { get; set; } = string.Empty;

    public List<int> SunriseImages { get; set; } = new();
    public List<int> DayImages { get; set; } = new();
    public List<int> SunsetImages { get; set; } = new();
    public List<int> NightImages { get; set; } = new();

    public Dictionary<int, MediaItem> ImageMap { get; set; } = new();
    public List<MediaItem> OrderedCycleItems { get; set; } = new();
}
