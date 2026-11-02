using System;
using System.Text.Json.Serialization;

namespace LivelyScreen.Models;

public class ArtworkMetadata
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("artist")]
    public string? Artist { get; set; }

    [JsonPropertyName("credit")]
    public string? Credit { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonIgnore]
    public bool HasDisplayableInfo =>
        !string.IsNullOrWhiteSpace(Title) ||
        !string.IsNullOrWhiteSpace(Artist) ||
        !string.IsNullOrWhiteSpace(Credit);

    public string GetFormattedCredit()
    {
        bool hasArtist = !string.IsNullOrWhiteSpace(Artist);
        bool hasCredit = !string.IsNullOrWhiteSpace(Credit);
        bool hasYear = !string.IsNullOrWhiteSpace(Year);

        string artistPart = hasArtist ? $"by {Artist}" : "";
        if (hasYear && hasArtist)
        {
            artistPart += $" ({Year})";
        }

        if (hasArtist && hasCredit)
        {
            return $"{artistPart} • {Credit}";
        }
        if (hasArtist)
        {
            return artistPart;
        }
        if (hasCredit)
        {
            return Credit!;
        }
        if (hasYear)
        {
            return Year!;
        }
        return "";
    }
}
