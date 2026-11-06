using System.Diagnostics;
using System.Security.Cryptography;

namespace LivelyScreen.Services.Dashboard;

public sealed class FileReconSampler : IDisposable
{
    public static FileReconSampler Shared { get; } = new();

    public const int MaxTreeLines = 40;
    public const int MaxHexLines = 40;
    public const double ModeDurationSeconds = 12.0;

    private readonly object _gate = new();
    private readonly RingBuffer<TreeReconLine> _treeBuffer = new(MaxTreeLines);
    private readonly RingBuffer<HexDumpLine> _hexBuffer = new(MaxHexLines);
    private readonly Queue<string> _pendingBinaryFiles = new();

    private CancellationTokenSource? _cts;
    private Task? _crawlTask;
    private int _subscriberCount;

    private ReconDisplayMode _currentMode = ReconDisplayMode.TreeF;
    private double _modeTimer = ModeDurationSeconds;
    private string _currentTarget = "C:\\Windows\\System32";
    private long _lastUpdateTicks;

    public event Action? ReconUpdated;

    public void AddRef()
    {
        lock (_gate)
        {
            _subscriberCount++;
            if (_subscriberCount == 1)
            {
                StartInternal();
            }
        }
    }

    public void ReleaseRef()
    {
        lock (_gate)
        {
            if (_subscriberCount > 0)
            {
                _subscriberCount--;
            }
            if (_subscriberCount == 0)
            {
                StopInternal();
            }
        }
    }

    private void StartInternal()
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        _lastUpdateTicks = Stopwatch.GetTimestamp();
        CancellationToken token = _cts.Token;
        _crawlTask = Task.Run(() => CrawlLoopAsync(token), token);
    }

    private void StopInternal()
    {
        var cts = _cts;
        _cts = null;
        if (cts != null)
        {
            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
        }
        _crawlTask = null;
    }

    public void AdvanceTime(double dtSeconds)
    {
        if (dtSeconds <= 0.0 || double.IsNaN(dtSeconds) || double.IsInfinity(dtSeconds))
            return;

        lock (_gate)
        {
            _modeTimer -= dtSeconds;
            if (_modeTimer <= 0.0)
            {
                _currentMode = _currentMode == ReconDisplayMode.TreeF
                    ? ReconDisplayMode.HexDump
                    : ReconDisplayMode.TreeF;
                _modeTimer = ModeDurationSeconds;
            }
        }
    }

    public ReconSnapshot GetSnapshot(bool maskSensitive)
    {
        lock (_gate)
        {
            var treeList = new List<TreeReconLine>(_treeBuffer.Count);
            for (int i = 0; i < _treeBuffer.Count; i++)
            {
                var item = _treeBuffer[i];
                if (maskSensitive)
                {
                    item = item with { Name = DashboardFormatters.MaskPath(item.Name, true) };
                }
                treeList.Add(item);
            }

            var hexList = new List<HexDumpLine>(_hexBuffer.Count);
            for (int i = 0; i < _hexBuffer.Count; i++)
            {
                var item = _hexBuffer[i];
                if (maskSensitive)
                {
                    item = item with { SourceName = DashboardFormatters.MaskPath(item.SourceName, true) };
                }
                hexList.Add(item);
            }

            string target = maskSensitive
                ? DashboardFormatters.MaskPath(_currentTarget, true)
                : _currentTarget;

            return new ReconSnapshot
            {
                Mode = _currentMode,
                SecondsRemainingInMode = Math.Max(0.0, _modeTimer),
                CurrentTarget = target,
                TreeLines = treeList,
                HexLines = hexList
            };
        }
    }

    private async Task CrawlLoopAsync(CancellationToken ct)
    {
        // Candidate directory roots to cycle through
        var candidateRoots = GetCandidateRoots();
        int rootIndex = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                string root = candidateRoots[rootIndex % candidateRoots.Count];
                rootIndex++;

                lock (_gate)
                {
                    _currentTarget = root;
                    _treeBuffer.Add(new TreeReconLine
                    {
                        Branch = "📁 ",
                        Name = root,
                        Tag = "[INDEX_ROOT]",
                        Severity = DashboardEventSeverity.Normal,
                        IsDirectory = true
                    });
                }
                ReconUpdated?.Invoke();

                await CrawlDirectoryRecursiveAsync(root, depth: 0, maxDepth: 3, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Soft skip on inaccessible root
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task CrawlDirectoryRecursiveAsync(string dirPath, int depth, int maxDepth, CancellationToken ct)
    {
        if (depth > maxDepth || ct.IsCancellationRequested)
            return;

        DirectoryInfo dirInfo;
        try
        {
            dirInfo = new DirectoryInfo(dirPath);
            if (!dirInfo.Exists) return;
        }
        catch
        {
            return;
        }

        FileSystemInfo[] entries;
        try
        {
            entries = dirInfo.GetFileSystemInfos();
        }
        catch (UnauthorizedAccessException)
        {
            lock (_gate)
            {
                _treeBuffer.Add(new TreeReconLine
                {
                    Branch = GetBranchPrefix(depth, isLast: true),
                    Name = Path.GetFileName(dirPath) + " [ACCESS DENIED]",
                    Tag = "[BYPASS_SKIP]",
                    Severity = DashboardEventSeverity.Warning,
                    IsDirectory = true
                });
            }
            ReconUpdated?.Invoke();
            await Task.Delay(160, ct).ConfigureAwait(false);
            return;
        }
        catch
        {
            return;
        }

        // Limit entries per folder to keep pacing lively
        var selected = entries.Take(12).ToList();
        for (int i = 0; i < selected.Count; i++)
        {
            if (ct.IsCancellationRequested) return;

            var entry = selected[i];
            bool isLast = i == selected.Count - 1;
            string branch = GetBranchPrefix(depth, isLast);
            string name = entry.Name;

            if (entry is DirectoryInfo subDir)
            {
                lock (_gate)
                {
                    _currentTarget = subDir.FullName;
                    _treeBuffer.Add(new TreeReconLine
                    {
                        Branch = branch,
                        Name = name + "\\",
                        Tag = "[DIR]",
                        Severity = DashboardEventSeverity.Normal,
                        IsDirectory = true
                    });
                }
                ReconUpdated?.Invoke();
                await Task.Delay(140, ct).ConfigureAwait(false);

                // Recurse into subdirectory
                await CrawlDirectoryRecursiveAsync(subDir.FullName, depth + 1, maxDepth, ct).ConfigureAwait(false);
            }
            else if (entry is FileInfo fileInfo)
            {
                string tag = GenerateCyberTag(fileInfo);
                DashboardEventSeverity severity = tag.Contains("CRIT", StringComparison.OrdinalIgnoreCase)
                    ? DashboardEventSeverity.Critical
                    : (tag.Contains("WARN", StringComparison.OrdinalIgnoreCase) ? DashboardEventSeverity.Warning : DashboardEventSeverity.Normal);

                lock (_gate)
                {
                    _currentTarget = fileInfo.FullName;
                    _treeBuffer.Add(new TreeReconLine
                    {
                        Branch = branch,
                        Name = name,
                        Tag = tag,
                        Severity = severity,
                        IsDirectory = false
                    });
                }
                ReconUpdated?.Invoke();

                // If candidate binary, queue for hex inspection
                if (IsInspectableBinary(fileInfo.Extension))
                {
                    await InspectBinaryFileAsync(fileInfo, ct).ConfigureAwait(false);
                }

                await Task.Delay(130, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task InspectBinaryFileAsync(FileInfo fileInfo, CancellationToken ct)
    {
        byte[] buffer = new byte[128];
        int read;
        try
        {
            using var fs = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            read = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (read <= 0) return;

        string sourceName = fileInfo.Name;
        lock (_gate)
        {
            for (int offset = 0; offset < read; offset += 16)
            {
                int len = Math.Min(16, read - offset);
                var slice = buffer.AsSpan(offset, len);
                string hex = DashboardFormatters.FormatHexLineBytes(slice);
                string ascii = DashboardFormatters.FormatAsciiGutter(slice);

                _hexBuffer.Add(new HexDumpLine
                {
                    Offset = (uint)offset,
                    HexBytes = hex,
                    AsciiGutter = ascii,
                    SourceName = sourceName
                });
            }
        }
        ReconUpdated?.Invoke();
    }

    private static string GetBranchPrefix(int depth, bool isLast)
    {
        string indent = depth > 0 ? new string(' ', depth * 3) : string.Empty;
        return isLast ? indent + "└── " : indent + "├── ";
    }

    private static string GenerateCyberTag(FileInfo file)
    {
        string ext = file.Extension.ToLowerInvariant();
        if (ext is ".sys" or ".drv")
            return "[SYS_DRIVER]";
        if (ext is ".dll")
            return $"[PE_LINK // {file.Length / 1024}KB]";
        if (ext is ".exe")
            return "[ENTRY_BIN]";
        if (ext is ".json" or ".xml" or ".config")
            return "[MANIFEST_OK]";
        if (ext is ".db" or ".sqlite")
            return "[SQLITE_DB]";
        if (ext is ".log")
            return "[LOG_STREAM]";

        return $"[SHA256:{(uint)file.Name.GetHashCode():x4}]";
    }

    private static bool IsInspectableBinary(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".dll" or ".exe" or ".sys" or ".mui" or ".dat" or ".bin" or ".db" or ".ico" or ".png" or ".xml" => true,
            _ => false
        };
    }

    private static List<string> GetCandidateRoots()
    {
        var roots = new List<string>();

        string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (Directory.Exists(systemDir))
        {
            string drivers = Path.Combine(systemDir, "drivers");
            if (Directory.Exists(drivers)) roots.Add(drivers);
            string wbem = Path.Combine(systemDir, "wbem");
            if (Directory.Exists(wbem)) roots.Add(wbem);
            roots.Add(systemDir);
        }

        string baseDir = AppContext.BaseDirectory;
        if (Directory.Exists(baseDir))
        {
            roots.Add(baseDir);
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (Directory.Exists(programFiles))
        {
            roots.Add(programFiles);
        }

        if (roots.Count == 0)
        {
            roots.Add("C:\\Windows");
        }

        return roots;
    }

    public void Dispose()
    {
        StopInternal();
    }
}
