using Microsoft.UI.Xaml;

namespace LivelyScreen.Models;

public enum MediaType
{
    Image,
    Video
}

public class MediaItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public MediaType Type { get; set; }
    public bool IsVideo => Type == MediaType.Video;
    public bool IsImage => Type == MediaType.Image;

    public MediaItem(string filePath, MediaType type)
    {
        FilePath = filePath;
        FileName = System.IO.Path.GetFileName(filePath);
        Type = type;
    }
}

public class MediaSourceItem
{
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAllSources { get; set; }
    public bool IsDynamicTheme { get; set; }
    public string BadgeText { get; set; } = string.Empty;
    public string Glyph { get; set; } = "\uE8B7";
    public string ItemCountText { get; set; } = string.Empty;
    public string PathText { get; set; } = string.Empty;
    public string DetailsText { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool CanSetActive => !IsActive;
    public string ActionButtonText => IsActive ? "✓ Active" : "Set Active";
    public Visibility ActiveBadgeVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RemoveVisibility => IsAllSources ? Visibility.Collapsed : Visibility.Visible;
    public SourceProfile Profile { get; set; } = new();
    public MediaFitMode FitMode
    {
        get => Profile.FitMode;
        set => Profile.FitMode = value;
    }
    public string FitButtonText => FitMode switch
    {
        MediaFitMode.Fill => "Fill Screen",
        MediaFitMode.Fit => "Fit to Screen",
        MediaFitMode.Stretch => "Stretch to Fill",
        MediaFitMode.Center => "Center",
        _ => "Fit ▾"
    };
}



