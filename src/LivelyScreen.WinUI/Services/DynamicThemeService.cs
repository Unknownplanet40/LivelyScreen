using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using LivelyScreen.Models;

namespace LivelyScreen.Services;

public static class DynamicThemeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static string ThemesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LivelyScreen",
        "Themes"
    );

    public static DynamicTheme? TryLoadThemeFromFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return null;

        try
        {
            WddThemeJson? wdd = null;

            // 1. Check loose theme.json or *.json in the folder
            string? jsonPath = Path.Combine(folderPath, "theme.json");
            if (!File.Exists(jsonPath))
            {
                jsonPath = Directory.EnumerateFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
            }

            if (jsonPath != null && File.Exists(jsonPath))
            {
                try
                {
                    var jsonText = File.ReadAllText(jsonPath);
                    wdd = JsonSerializer.Deserialize<WddThemeJson>(jsonText, JsonOptions);
                }
                catch { }
            }

            // 2. If loose JSON is missing or lacks day/night image lists, check any .lsdt, .ldt, or archive file in the folder
            bool lacksLists = wdd == null ||
                ((wdd.DayImageList == null || wdd.DayImageList.Length == 0) &&
                 (wdd.NightImageList == null || wdd.NightImageList.Length == 0));

            if (lacksLists)
            {
                var packageFile = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => {
                        var e = Path.GetExtension(f).ToLowerInvariant();
                        return e == ".lsdt" || e == ".ldt" || e == ".ddw" || e == ".zip";
                    });

                if (packageFile != null)
                {
                    try
                    {
                        using var archive = ZipFile.OpenRead(packageFile);
                        var jsonEntry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                        if (jsonEntry != null)
                        {
                            using var sr = new StreamReader(jsonEntry.Open());
                            var pkgJsonText = sr.ReadToEnd();
                            var pkgParsed = JsonSerializer.Deserialize<WddThemeJson>(pkgJsonText, JsonOptions);
                            if (pkgParsed != null && !string.IsNullOrWhiteSpace(pkgParsed.ImageFilename))
                            {
                                wdd = pkgParsed;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LivelyScreen_WinUI.App.Log($"Error reading package in {folderPath}: {ex.Message}");
                    }
                }
            }

            if (wdd == null || string.IsNullOrWhiteSpace(wdd.ImageFilename))
                return null;

            var folderName = new DirectoryInfo(folderPath).Name;
            var displayName = !string.IsNullOrWhiteSpace(wdd.DisplayName)
                ? wdd.DisplayName
                : folderName.Replace('_', ' ');

            var theme = new DynamicTheme
            {
                Id = folderPath,
                DisplayName = displayName,
                FolderPath = folderPath,
                ImageFilenamePattern = wdd.ImageFilename.Trim(),
                Credits = wdd.ImageCredits ?? "Unknown"
            };

            // Build regex to extract image number from pattern like "firewatch_*.jpg" or "Wp_*.png"
            var pattern = theme.ImageFilenamePattern;
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", "(\\d+)") + "$";
            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

            foreach (var file in Directory.EnumerateFiles(folderPath))
            {
                var fileName = Path.GetFileName(file);
                var match = regex.Match(fileName);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int imgNum))
                {
                    theme.ImageMap[imgNum] = new MediaItem(file, MediaType.Image);
                }
            }

            if (theme.ImageMap.Count == 0)
                return null;

            if (wdd.SunriseImageList != null)
                theme.SunriseImages.AddRange(wdd.SunriseImageList);
            if (wdd.DayImageList != null)
                theme.DayImages.AddRange(wdd.DayImageList);
            if (wdd.SunsetImageList != null)
                theme.SunsetImages.AddRange(wdd.SunsetImageList);
            if (wdd.NightImageList != null)
                theme.NightImages.AddRange(wdd.NightImageList);

            // Fallback if lists were omitted in custom JSON
            if (theme.DayImages.Count == 0 && theme.NightImages.Count == 0)
            {
                var sortedNums = theme.ImageMap.Keys.OrderBy(k => k).ToList();
                int count = sortedNums.Count;
                if (count >= 4)
                {
                    int q = Math.Max(1, count / 4);
                    theme.SunriseImages.AddRange(sortedNums.Take(q));
                    theme.DayImages.AddRange(sortedNums.Skip(q).Take(q));
                    theme.SunsetImages.AddRange(sortedNums.Skip(q * 2).Take(q));
                    theme.NightImages.AddRange(sortedNums.Skip(q * 3));
                }
                else
                {
                    int half = Math.Max(1, count / 2);
                    theme.DayImages.AddRange(sortedNums.Take(half));
                    theme.NightImages.AddRange(sortedNums.Skip(half));
                }
            }

            // Build complete solar progression cycle: Sunrise -> Day -> Sunset -> Night
            var cycleIndices = new List<int>();
            cycleIndices.AddRange(theme.SunriseImages);
            cycleIndices.AddRange(theme.DayImages);
            cycleIndices.AddRange(theme.SunsetImages);
            cycleIndices.AddRange(theme.NightImages);

            foreach (var idx in cycleIndices)
            {
                if (theme.ImageMap.TryGetValue(idx, out var mediaItem))
                {
                    theme.OrderedCycleItems.Add(mediaItem);
                }
            }

            // Fallback if indices didn't match
            if (theme.OrderedCycleItems.Count == 0)
            {
                foreach (var kvp in theme.ImageMap.OrderBy(k => k.Key))
                {
                    theme.OrderedCycleItems.Add(kvp.Value);
                }
            }

            return theme;
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"TryLoadThemeFromFolder error in {folderPath}: {ex.Message}");
            return null;
        }
    }

    public static string? ImportThemeFile(string filePath)
    {
        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".json")
            {
                return Path.GetDirectoryName(filePath);
            }

            if (ext == ".lsdt" || ext == ".ldt" || ext == ".zip" || ext == ".ddw")
            {
                var themeName = Path.GetFileNameWithoutExtension(filePath);
                var destDir = Path.Combine(ThemesDirectory, themeName);
                if (Directory.Exists(destDir))
                {
                    Directory.Delete(destDir, recursive: true);
                }
                Directory.CreateDirectory(destDir);
                ZipFile.ExtractToDirectory(filePath, destDir);

                // If package had a single nested subfolder, resolve it
                var subDirs = Directory.GetDirectories(destDir);
                if (subDirs.Length == 1 && Directory.GetFiles(destDir).Length == 0)
                {
                    return subDirs[0];
                }
                return destDir;
            }
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"ImportThemeFile error: {ex.Message}");
        }
        return null;
    }

    public static bool ExportThemePackage(string folderPath, string destinationFilePath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
                return false;

            if (File.Exists(destinationFilePath))
            {
                File.Delete(destinationFilePath);
            }

            var parentDir = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            ZipFile.CreateFromDirectory(folderPath, destinationFilePath, CompressionLevel.Optimal, includeBaseDirectory: false);
            LivelyScreen_WinUI.App.Log($"Dynamic Theme packaged successfully to: {destinationFilePath}");
            return true;
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"ExportThemePackage error: {ex.Message}");
            return false;
        }
    }
}
