using System.IO;
using LivelyScreen.Models;
using LivelyScreen.Native;

namespace LivelyScreen.Services;

public class MediaLibrary
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".webm", ".mov", ".avi", ".wmv", ".m4v"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"
    };

    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "thumbnail.png", "thumbnail.jpg", "folder.jpg", "desktop.ini"
    };

    private readonly SettingsService _settingsService;
    private readonly List<MediaItem> _items = new();
    private readonly List<DynamicTheme> _themes = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly object _lock = new();
    private readonly Random _rng = new();
    private readonly Dictionary<string, int> _sourceSequenceIndices = new(StringComparer.OrdinalIgnoreCase);
    private PlaybackOrder _lastOrder = (PlaybackOrder)(-1);

    public int TotalMediaCount
    {
        get
        {
            lock (_lock) return _items.Count;
        }
    }

    public List<DynamicTheme> GetThemes()
    {
        lock (_lock)
        {
            return _themes.ToList();
        }
    }

    public DynamicTheme? GetActiveTheme(string? overrideSourcePath = null)
    {
        lock (_lock)
        {
            if (!_settingsService.Current.DynamicThemeEnabled)
                return null;

            if (_themes.Count == 0)
                return null;

            var activeSource = !string.IsNullOrEmpty(overrideSourcePath)
                ? overrideSourcePath
                : _settingsService.Current.ActiveSourcePath;

            if (!string.IsNullOrEmpty(activeSource) && !string.Equals(activeSource, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                var match = _themes.FirstOrDefault(t => string.Equals(t.Id, activeSource, StringComparison.OrdinalIgnoreCase) ||
                                                        string.Equals(t.FolderPath, activeSource, StringComparison.OrdinalIgnoreCase));
                return match;
            }

            if (string.Equals(activeSource, "ALL", StringComparison.OrdinalIgnoreCase))
                return null;

            var selectedId = _settingsService.Current.SelectedThemeId;
            if (string.Equals(selectedId, "NONE", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!string.IsNullOrEmpty(selectedId))
            {
                var match = _themes.FirstOrDefault(t => string.Equals(t.Id, selectedId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            return null;
        }
    }

    public List<MediaSourceItem> GetMediaSources()
    {
        lock (_lock)
        {
            var list = new List<MediaSourceItem>();
            var activePath = _settingsService.Current.ActiveSourcePath;
            if (string.IsNullOrEmpty(activePath))
                activePath = "ALL";

            int totalPhotos = _items.Count(i => i.Type == MediaType.Image);
            int totalVideos = _items.Count(i => i.Type == MediaType.Video);
            string allCountText = totalPhotos > 0 && totalVideos > 0 
                ? $"{_items.Count} items ({totalPhotos} photos, {totalVideos} videos)"
                : totalVideos > 0 ? $"{totalVideos} videos" : $"{totalPhotos} photos";

            list.Add(new MediaSourceItem
            {
                Path = "ALL",
                DisplayName = "All Sources (Combined)",
                IsAllSources = true,
                IsDynamicTheme = false,
                BadgeText = "Combined Playlist",
                Glyph = "\uE8EC",
                ItemCountText = allCountText,
                PathText = "All configured folders",
                DetailsText = $"{allCountText} across all watched folders",
                IsActive = string.Equals(activePath, "ALL", StringComparison.OrdinalIgnoreCase),
                Profile = _settingsService.Current.GetProfileForPath("ALL")
            });

            foreach (var folder in _settingsService.Current.MediaFolders)
            {
                if (!Directory.Exists(folder))
                    continue;

                var theme = _themes.FirstOrDefault(t => string.Equals(t.FolderPath, folder, StringComparison.OrdinalIgnoreCase));
                if (theme == null)
                {
                    theme = DynamicThemeService.TryLoadThemeFromFolder(folder);
                }

                bool isActive = string.Equals(activePath, folder, StringComparison.OrdinalIgnoreCase);

                if (theme != null)
                {
                    int frames = theme.ImageMap.Count;
                    string countText = frames == 1 ? "1 solar frame" : $"{frames} solar frames";
                    if (theme.DayImages.Count > 0 || theme.NightImages.Count > 0)
                    {
                        countText += $" ({theme.DayImages.Count} Day, {theme.NightImages.Count} Night)";
                    }

                    list.Add(new MediaSourceItem
                    {
                        Path = folder,
                        DisplayName = theme.DisplayName,
                        IsAllSources = false,
                        IsDynamicTheme = true,
                        BadgeText = "Dynamic Theme",
                        Glyph = "\uE706",
                        ItemCountText = countText,
                        PathText = folder,
                        DetailsText = $"{countText} • Solar Synced • {folder}",
                        IsActive = isActive,
                        Profile = _settingsService.Current.GetProfileForPath(folder)
                    });
                }
                else
                {
                    var folderItems = _items.Where(i => i.FilePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase)).ToList();
                    int photoCount = folderItems.Count(i => i.Type == MediaType.Image);
                    int videoCount = folderItems.Count(i => i.Type == MediaType.Video);

                    string badge;
                    string glyph;
                    string countText;

                    if (photoCount > 0 && videoCount == 0)
                    {
                        badge = "Photos";
                        glyph = "\uEB9F";
                        countText = photoCount == 1 ? "1 photo" : $"{photoCount} photos";
                    }
                    else if (videoCount > 0 && photoCount == 0)
                    {
                        badge = "Videos";
                        glyph = "\uE714";
                        countText = videoCount == 1 ? "1 video" : $"{videoCount} videos";
                    }
                    else if (photoCount > 0 && videoCount > 0)
                    {
                        badge = "Photos & Videos";
                        glyph = "\uEC4F";
                        countText = $"{photoCount} photo{(photoCount == 1 ? "" : "s")}, {videoCount} video{(videoCount == 1 ? "" : "s")}";
                    }
                    else
                    {
                        badge = "Empty";
                        glyph = "\uE8B7";
                        countText = "0 items";
                    }

                    var folderName = new DirectoryInfo(folder).Name;
                    list.Add(new MediaSourceItem
                    {
                        Path = folder,
                        DisplayName = folderName,
                        IsAllSources = false,
                        IsDynamicTheme = false,
                        BadgeText = badge,
                        Glyph = glyph,
                        ItemCountText = countText,
                        PathText = folder,
                        DetailsText = $"{countText} • {folder}",
                        IsActive = isActive,
                        Profile = _settingsService.Current.GetProfileForPath(folder)
                    });
                }
            }

            return list;
        }
    }

    public List<MediaSourceItem> GetDynamicThemeSources()
    {
        return GetMediaSources().Where(s => s.IsDynamicTheme).ToList();
    }

    public List<MediaSourceItem> GetStandardFolderSources()
    {
        return GetMediaSources().Where(s => !s.IsDynamicTheme && !s.IsAllSources).ToList();
    }

    public event Action? LibraryUpdated;

    public MediaLibrary(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _settingsService.SettingsChanged += _ => Rescan();
        Rescan();
    }

    public void Rescan()
    {
        Task.Run(() =>
        {
            lock (_lock)
            {
                foreach (var watcher in _watchers)
                {
                    try { watcher.Dispose(); } catch { }
                }
                _watchers.Clear();
                _items.Clear();
                _themes.Clear();

                var folders = new List<string>(_settingsService.Current.MediaFolders);
                if (Directory.Exists(DynamicThemeService.ThemesDirectory) &&
                    !folders.Contains(DynamicThemeService.ThemesDirectory, StringComparer.OrdinalIgnoreCase))
                {
                    folders.Add(DynamicThemeService.ThemesDirectory);
                }

                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder))
                        continue;

                    try
                    {
                        ScanFolder(folder);

                        var watcher = new FileSystemWatcher(folder)
                        {
                            IncludeSubdirectories = true,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                            EnableRaisingEvents = true
                        };
                        watcher.Created += (_, _) => OnFolderChanged();
                        watcher.Deleted += (_, _) => OnFolderChanged();
                        watcher.Renamed += (_, _) => OnFolderChanged();
                        _watchers.Add(watcher);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to watch {folder}: {ex.Message}");
                    }
                }

                SortItems(_settingsService.Current.Order);
                _lastOrder = _settingsService.Current.Order;
            }

            LibraryUpdated?.Invoke();
        });
    }

    private void ScanFolder(string rootFolder)
    {
        try
        {
            var rootTheme = DynamicThemeService.TryLoadThemeFromFolder(rootFolder);
            if (rootTheme != null && !_themes.Any(t => string.Equals(t.Id, rootTheme.Id, StringComparison.OrdinalIgnoreCase)))
            {
                _themes.Add(rootTheme);
            }

            var subDirs = Directory.EnumerateDirectories(rootFolder, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            });

            foreach (var subDir in subDirs)
            {
                var subTheme = DynamicThemeService.TryLoadThemeFromFolder(subDir);
                if (subTheme != null && !_themes.Any(t => string.Equals(t.Id, subTheme.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    _themes.Add(subTheme);
                }
            }

            // Always scan standard videos and photos in configured MediaFolders so Standard Folder mode works seamlessly
            if (!_settingsService.Current.MediaFolders.Contains(rootFolder, StringComparer.OrdinalIgnoreCase))
                return;

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            };

            var files = Directory.EnumerateFiles(rootFolder, "*.*", options);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (IgnoredFileNames.Contains(fileName))
                    continue;

                var ext = Path.GetExtension(file);
                if (VideoExtensions.Contains(ext))
                {
                    _items.Add(new MediaItem(file, MediaType.Video));
                }
                else if (ImageExtensions.Contains(ext))
                {
                    _items.Add(new MediaItem(file, MediaType.Image));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error scanning folder {rootFolder}: {ex.Message}");
        }
    }

    private void OnFolderChanged()
    {
        Task.Delay(500).ContinueWith(_ => Rescan());
    }

    public void ResetSequenceIndex()
    {
        lock (_lock)
        {
            _sourceSequenceIndices.Clear();
        }
    }

    public MediaItem? GetNextItem(PlaybackOrder order, string? overrideSourcePath = null)
    {
        lock (_lock)
        {
            string sourceKey = string.IsNullOrWhiteSpace(overrideSourcePath)
                ? (_settingsService.Current.ActiveSourcePath ?? "ALL")
                : overrideSourcePath;

            var activeTheme = GetActiveTheme(overrideSourcePath);
            if (activeTheme != null)
            {
                var mode = _settingsService.Current.ThemeMode;
                if (mode == DynamicThemeMode.SolarTimeLocked)
                {
                    var solar = SolarCalculator.GetCurrentSolarMedia(activeTheme, DateTime.Now, _settingsService.Current);
                    if (solar.Item != null)
                        return solar.Item;
                }
                else if (mode == DynamicThemeMode.SolarSegmentCycle)
                {
                    var solar = SolarCalculator.GetCurrentSolarMedia(activeTheme, DateTime.Now, _settingsService.Current);
                    var segNums = SolarCalculator.GetSegmentImageNumbers(activeTheme, solar.Segment);
                    if (segNums.Count > 0)
                    {
                        _sourceSequenceIndices.TryGetValue(sourceKey, out int curIdx);
                        int idx = curIdx % segNums.Count;
                        _sourceSequenceIndices[sourceKey] = (curIdx + 1) % segNums.Count;
                        int imgNum = segNums[idx];
                        if (activeTheme.ImageMap.TryGetValue(imgNum, out var segItem))
                            return segItem;
                    }
                    if (solar.Item != null)
                        return solar.Item;
                }
                else if (mode == DynamicThemeMode.FullDayTimeLapse && activeTheme.OrderedCycleItems.Count > 0)
                {
                    _sourceSequenceIndices.TryGetValue(sourceKey, out int curIdx);
                    int idx = curIdx % activeTheme.OrderedCycleItems.Count;
                    _sourceSequenceIndices[sourceKey] = (curIdx + 1) % activeTheme.OrderedCycleItems.Count;
                    return activeTheme.OrderedCycleItems[idx];
                }

                // If active dynamic theme is set, never fall through to standard items from other sources!
                if (activeTheme.OrderedCycleItems.Count > 0)
                    return activeTheme.OrderedCycleItems[0];
            }

            var candidateItems = string.IsNullOrEmpty(sourceKey) || string.Equals(sourceKey, "ALL", StringComparison.OrdinalIgnoreCase)
                ? _items.ToList()
                : _items.Where(i => i.FilePath.StartsWith(sourceKey, StringComparison.OrdinalIgnoreCase)).ToList();

            if (candidateItems.Count == 0)
                return null;

            var effectiveOrder = order;
            var profile = _settingsService.Current.GetProfileForPath(sourceKey);
            if (profile.Order.HasValue)
            {
                effectiveOrder = profile.Order.Value;
            }

            if (effectiveOrder == PlaybackOrder.Shuffle)
            {
                var index = _rng.Next(candidateItems.Count);
                return candidateItems[index];
            }

            SortItemList(candidateItems, effectiveOrder);

            _sourceSequenceIndices.TryGetValue(sourceKey, out int currentSourceIdx);
            if (currentSourceIdx >= candidateItems.Count)
                currentSourceIdx = 0;

            var item = candidateItems[currentSourceIdx];
            _sourceSequenceIndices[sourceKey] = (currentSourceIdx + 1) % candidateItems.Count;
            return item;
        }
    }

    public List<MediaItem> GetActiveItems()
    {
        lock (_lock)
        {
            var activeSource = _settingsService.Current.ActiveSourcePath;
            if (!string.IsNullOrEmpty(activeSource) && !string.Equals(activeSource, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                return _items.Where(i => i.FilePath.StartsWith(activeSource, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            return _items.ToList();
        }
    }

    public static void SortItemList(List<MediaItem> list, PlaybackOrder order)
    {
        switch (order)
        {
            case PlaybackOrder.Sequential:
                list.Sort((a, b) => NativeMethods.StrCmpLogicalW(a.FileName, b.FileName));
                break;
            case PlaybackOrder.Alphabetical:
                list.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
                break;
            case PlaybackOrder.DateModified:
                list.Sort((a, b) =>
                {
                    try
                    {
                        return File.GetLastWriteTimeUtc(b.FilePath).CompareTo(File.GetLastWriteTimeUtc(a.FilePath));
                    }
                    catch { return 0; }
                });
                break;
        }
    }

    public void SortItems(PlaybackOrder order)
    {
        lock (_lock)
        {
            SortItemList(_items, order);
        }
    }
}
