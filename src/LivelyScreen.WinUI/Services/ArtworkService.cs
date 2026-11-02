using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LivelyScreen.Models;
using LivelyScreen_WinUI;

namespace LivelyScreen.Services;

public static class ArtworkService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static bool IsArtworkFolder(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath)) return false;

            if (File.Exists(Path.Combine(folderPath, "artworks.json")) ||
                File.Exists(Path.Combine(folderPath, "metadata.json")))
            {
                return true;
            }

            var dirName = new DirectoryInfo(folderPath).Name;
            if (dirName.Contains("art", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check if there are any sidecar json files matching image names
            var jsonFiles = Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly);
            foreach (var jf in jsonFiles)
            {
                var name = Path.GetFileName(jf);
                if (name.Equals("theme.json", StringComparison.OrdinalIgnoreCase)) continue;
                return true;
            }
        }
        catch { }

        return false;
    }

    public static Dictionary<string, ArtworkMetadata> LoadFolderArtworkMetadata(string folderPath)
    {
        var dict = new Dictionary<string, ArtworkMetadata>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!Directory.Exists(folderPath)) return dict;

            // 1. Check artworks.json or metadata.json
            string[] manifestCandidates = { "artworks.json", "metadata.json" };
            foreach (var candidate in manifestCandidates)
            {
                string fullPath = Path.Combine(folderPath, candidate);
                if (File.Exists(fullPath))
                {
                    ParseManifestFile(fullPath, dict);
                    break;
                }
            }

            // 2. Check for sidecar JSON files (e.g. photo.jpg + photo.json)
            var jsonFiles = Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly);
            foreach (var jf in jsonFiles)
            {
                var baseName = Path.GetFileNameWithoutExtension(jf);
                if (baseName.Equals("artworks", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Equals("metadata", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Equals("theme", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!dict.ContainsKey(baseName))
                {
                    try
                    {
                        string json = File.ReadAllText(jf);
                        var meta = JsonSerializer.Deserialize<ArtworkMetadata>(json, JsonOptions);
                        if (meta != null)
                        {
                            dict[baseName] = meta;
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"Error loading artwork metadata in {folderPath}: {ex.Message}");
        }

        return dict;
    }

    private static void ParseManifestFile(string filePath, Dictionary<string, ArtworkMetadata> dict)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            var rootNode = JsonNode.Parse(json);
            if (rootNode == null) return;

            // Pattern A: { "artworks": [ { "file": "x.jpg", "title": "...", "artist": "..." } ] }
            if (rootNode is JsonObject rootObj && rootObj.TryGetPropertyValue("artworks", out var artworksNode) && artworksNode is JsonArray arr)
            {
                ParseArtworkArray(arr, dict);
                return;
            }

            // Pattern B: Raw array [ { "file": "x.jpg", "title": "..." } ]
            if (rootNode is JsonArray rawArr)
            {
                ParseArtworkArray(rawArr, dict);
                return;
            }

            // Pattern C: Key-Value dictionary { "x.jpg": { "title": "...", "artist": "..." } }
            if (rootNode is JsonObject dictObj)
            {
                foreach (var kvp in dictObj)
                {
                    if (kvp.Value is JsonObject itemObj)
                    {
                        var meta = itemObj.Deserialize<ArtworkMetadata>(JsonOptions);
                        if (meta != null)
                        {
                            string key = kvp.Key;
                            dict[key] = meta;
                            dict[Path.GetFileNameWithoutExtension(key)] = meta;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"Error parsing artwork manifest {filePath}: {ex.Message}");
        }
    }

    private static void ParseArtworkArray(JsonArray arr, Dictionary<string, ArtworkMetadata> dict)
    {
        foreach (var node in arr)
        {
            if (node is JsonObject obj)
            {
                string? fileKey = null;
                if (obj.TryGetPropertyValue("file", out var fNode) && fNode != null)
                    fileKey = fNode.ToString();
                else if (obj.TryGetPropertyValue("filename", out var fnNode) && fnNode != null)
                    fileKey = fnNode.ToString();
                else if (obj.TryGetPropertyValue("name", out var nNode) && nNode != null)
                    fileKey = nNode.ToString();

                var meta = obj.Deserialize<ArtworkMetadata>(JsonOptions);
                if (meta != null && !string.IsNullOrEmpty(fileKey))
                {
                    dict[fileKey] = meta;
                    dict[Path.GetFileNameWithoutExtension(fileKey)] = meta;
                }
            }
        }
    }

    public static ArtworkMetadata? GetArtworkForFile(string filePath, Dictionary<string, ArtworkMetadata>? folderMetadata, bool isArtworkFolder)
    {
        string fileName = Path.GetFileName(filePath);
        string baseName = Path.GetFileNameWithoutExtension(filePath);

        if (folderMetadata != null)
        {
            if (folderMetadata.TryGetValue(fileName, out var metaByFile))
                return metaByFile;
            if (folderMetadata.TryGetValue(baseName, out var metaByBase))
                return metaByBase;
        }

        // Direct sidecar check if not in cached dictionary
        string sidecarPath = Path.ChangeExtension(filePath, ".json");
        if (File.Exists(sidecarPath))
        {
            try
            {
                string json = File.ReadAllText(sidecarPath);
                var sidecarMeta = JsonSerializer.Deserialize<ArtworkMetadata>(json, JsonOptions);
                if (sidecarMeta != null)
                    return sidecarMeta;
            }
            catch { }
        }

        // If folder is explicitly an artwork gallery, fallback to filename as title
        if (isArtworkFolder)
        {
            return new ArtworkMetadata
            {
                Title = CleanFilenameToTitle(baseName)
            };
        }

        return null;
    }

    public static string CleanFilenameToTitle(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) return "";
        // Replace underscores, hyphens, and dot-separated words with clean spaces
        string cleaned = baseName.Replace('_', ' ').Replace('-', ' ');
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned.Trim());
    }

    public static async Task CreateStarterArtworkFolderAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string manifestPath = Path.Combine(folderPath, "artworks.json");
        if (!File.Exists(manifestPath))
        {
            var starter = new
            {
                description = "Artwork Gallery metadata: define captions, artist credits, and years for images in this folder.",
                artworks = new[]
                {
                    new
                    {
                        file = "example.jpg",
                        title = "No help is comming",
                        artist = "PQHAUS",
                        credit = "ctto",
                        year = "2024"
                    }
                }
            };

            string json = JsonSerializer.Serialize(starter, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, json);
        }
    }

    public static async Task<string> SaveArtworkEntryAsync(string galleryFolder, string sourceImagePath, ArtworkMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(galleryFolder))
        {
            throw new ArgumentException("Target gallery folder cannot be empty.", nameof(galleryFolder));
        }

        if (string.IsNullOrWhiteSpace(sourceImagePath))
        {
            throw new ArgumentException("Artwork image path cannot be empty.", nameof(sourceImagePath));
        }

        galleryFolder = Path.GetFullPath(galleryFolder.Trim().Trim('"', '\''));
        sourceImagePath = Path.GetFullPath(sourceImagePath.Trim().Trim('"', '\''));

        if (!File.Exists(sourceImagePath))
        {
            throw new FileNotFoundException($"Source image file not found: {sourceImagePath}");
        }

        if (!Directory.Exists(galleryFolder))
        {
            Directory.CreateDirectory(galleryFolder);
        }

        string fileName = Path.GetFileName(sourceImagePath);
        string destFilePath = Path.Combine(galleryFolder, fileName);

        if (!string.Equals(sourceImagePath, destFilePath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Copy(sourceImagePath, destFilePath, overwrite: true);
            }
            catch (Exception exCopy)
            {
                App.Log($"Standard File.Copy failed ({exCopy.Message}), attempting stream copy fallback...");
                using var srcStream = new FileStream(sourceImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var dstStream = new FileStream(destFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                await srcStream.CopyToAsync(dstStream);
            }
        }

        string manifestPath = Path.Combine(galleryFolder, "artworks.json");
        var manifest = new ArtworkManifestDto();

        try
        {
            if (File.Exists(manifestPath))
            {
                string json = await File.ReadAllTextAsync(manifestPath);
                var loaded = JsonSerializer.Deserialize<ArtworkManifestDto>(json, JsonOptions);
                if (loaded?.Artworks != null)
                {
                    manifest = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"Error reading existing manifest {manifestPath}: {ex.Message}");
        }

        var existing = manifest.Artworks.FirstOrDefault(a => string.Equals(a.File, fileName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Title = metadata.Title ?? string.Empty;
            existing.Artist = metadata.Artist;
            existing.Credit = metadata.Credit;
            existing.Year = metadata.Year;
        }
        else
        {
            manifest.Artworks.Add(new ArtworkEntryDto
            {
                File = fileName,
                Title = metadata.Title ?? string.Empty,
                Artist = metadata.Artist,
                Credit = metadata.Credit,
                Year = metadata.Year
            });
        }

        var writeOptions = new JsonSerializerOptions { WriteIndented = true };
        string outputJson = JsonSerializer.Serialize(manifest, writeOptions);
        await File.WriteAllTextAsync(manifestPath, outputJson);

        try
        {
            string sidecarPath = Path.ChangeExtension(destFilePath, ".json");
            string sidecarJson = JsonSerializer.Serialize(metadata, writeOptions);
            await File.WriteAllTextAsync(sidecarPath, sidecarJson);
        }
        catch { }

        return destFilePath;
    }
    public static List<ArtworkManageItem> GetGalleryArtworks(string folderPath)
    {
        var list = new List<ArtworkManageItem>();
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return list;

        var metaDict = LoadFolderArtworkMetadata(folderPath);
        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif";
            })
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string fileName = Path.GetFileName(file);
            string baseName = Path.GetFileNameWithoutExtension(file);

            ArtworkMetadata? meta = null;
            if (metaDict.TryGetValue(fileName, out var m1)) meta = m1;
            else if (metaDict.TryGetValue(baseName, out var m2)) meta = m2;

            string title = meta != null && !string.IsNullOrWhiteSpace(meta.Title)
                ? meta.Title
                : CleanFilenameToTitle(baseName);
            string artist = meta?.Artist ?? "";
            string credit = meta?.Credit ?? "";
            string year = meta?.Year ?? "";

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(artist)) parts.Add($"by {artist}");
            if (!string.IsNullOrWhiteSpace(credit)) parts.Add(credit);
            if (!string.IsNullOrWhiteSpace(year)) parts.Add(year);
            string creditLine = parts.Count > 0 ? string.Join(" • ", parts) : "No credit info";

            list.Add(new ArtworkManageItem
            {
                GalleryFolder = folderPath,
                FileName = fileName,
                FullImagePath = file,
                Title = title,
                Artist = artist,
                Credit = credit,
                Year = year,
                FormattedCreditLine = creditLine
            });
        }

        return list;
    }

    public static async Task DeleteArtworkEntryAsync(string galleryFolder, string fileName, bool deleteImageFile = false)
    {
        if (string.IsNullOrWhiteSpace(galleryFolder) || !Directory.Exists(galleryFolder)) return;

        string manifestPath = Path.Combine(galleryFolder, "artworks.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(manifestPath);
                var loaded = JsonSerializer.Deserialize<ArtworkManifestDto>(json, JsonOptions);
                if (loaded?.Artworks != null)
                {
                    loaded.Artworks.RemoveAll(a => string.Equals(a.File, fileName, StringComparison.OrdinalIgnoreCase));
                    var writeOptions = new JsonSerializerOptions { WriteIndented = true };
                    await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(loaded, writeOptions));
                }
            }
            catch (Exception ex)
            {
                App.Log($"Error removing entry from {manifestPath}: {ex.Message}");
            }
        }

        string fullImagePath = Path.Combine(galleryFolder, fileName);
        string sidecarPath = Path.ChangeExtension(fullImagePath, ".json");
        try
        {
            if (File.Exists(sidecarPath)) File.Delete(sidecarPath);
        }
        catch { }

        if (deleteImageFile)
        {
            try
            {
                if (File.Exists(fullImagePath)) File.Delete(fullImagePath);
            }
            catch { }
        }
    }

}

public class ArtworkManifestDto
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = "Artwork Gallery metadata: define captions, artist credits, and years for images in this folder.";

    [JsonPropertyName("artworks")]
    public List<ArtworkEntryDto> Artworks { get; set; } = new();
}

public class ArtworkEntryDto
{
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("artist")]
    public string? Artist { get; set; }

    [JsonPropertyName("credit")]
    public string? Credit { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }
}

public class ArtworkManageItem
{
    public string GalleryFolder { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FullImagePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Credit { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public string FormattedCreditLine { get; set; } = string.Empty;
}
