using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Dispatching;
using LivelyScreen_WinUI.Services;

namespace LivelyScreen.Services;

public class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = new();
}

public class ReleaseInfo
{
    public string TagName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Changelog { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public string? DownloadUrl { get; set; }
    public long DownloadSize { get; set; }
    public Version Version { get; set; } = new Version(0, 0, 0);
    public bool IsNewerThanCurrent { get; set; }
}

public enum UpdateState
{
    Idle,
    Checking,
    UpdateAvailable,
    UpToDate,
    Downloading,
    Downloaded,
    Failed
}

public class UpdateService
{
    private const string RepoOwner = "Unknownplanet40";
    private const string RepoName = "LivelyScreen";
    private const string LatestReleaseUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private readonly HttpClient _httpClient;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Microsoft.UI.Xaml.DispatcherTimer _periodicTimer;
    private readonly Microsoft.UI.Xaml.DispatcherTimer _startupTimer;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public ReleaseInfo? LatestRelease { get; private set; }
    public string? DownloadedInstallerPath { get; private set; }
    public double DownloadProgress { get; private set; }
    public string StatusMessage { get; private set; } = string.Empty;

    public event Action<UpdateState, string>? StateChanged;
    public event Action<double>? DownloadProgressChanged;

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v != null ? new Version(v.Major, v.Minor, v.Build) : new Version(1, 0, 0);
        }
    }

    public static string CurrentVersionString => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public UpdateService(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LivelyScreen-App");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        // Periodic background check every 24 hours
        _periodicTimer = new Microsoft.UI.Xaml.DispatcherTimer
        {
            Interval = TimeSpan.FromHours(24)
        };
        _periodicTimer.Tick += async (_, _) => await CheckForUpdatesAsync(silentBackground: true);

        // Initial background check 12 seconds after launch to ensure smooth startup
        _startupTimer = new Microsoft.UI.Xaml.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(12)
        };
        _startupTimer.Tick += async (_, _) =>
        {
            _startupTimer.Stop();
            await CheckForUpdatesAsync(silentBackground: true);
        };
    }

    public void StartBackgroundMonitoring()
    {
        _startupTimer.Start();
        _periodicTimer.Start();
    }

    public async Task<ReleaseInfo?> CheckForUpdatesAsync(bool silentBackground = false)
    {
        if (State == UpdateState.Checking || State == UpdateState.Downloading)
            return LatestRelease;

        SetState(UpdateState.Checking, "Checking GitHub for updates...");

        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseUrl);
            if (!response.IsSuccessStatusCode)
            {
                var err = $"GitHub API check returned {(int)response.StatusCode}: {response.ReasonPhrase}";
                LivelyScreen_WinUI.App.Log($"[UpdateService] {err}");
                SetState(UpdateState.Failed, "Unable to check for updates right now.");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var dto = JsonSerializer.Deserialize<GitHubReleaseDto>(json);
            if (dto == null || string.IsNullOrWhiteSpace(dto.TagName))
            {
                SetState(UpdateState.Failed, "Invalid release response from GitHub.");
                return null;
            }

            var remoteVersion = ParseVersion(dto.TagName);
            var isNewer = remoteVersion != null && remoteVersion > CurrentVersion;

            // Find setup installer asset (.exe)
            var setupAsset = dto.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                ?? dto.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            var info = new ReleaseInfo
            {
                TagName = dto.TagName,
                Title = string.IsNullOrWhiteSpace(dto.Name) ? dto.TagName : dto.Name,
                Changelog = dto.Body ?? string.Empty,
                HtmlUrl = dto.HtmlUrl,
                DownloadUrl = setupAsset?.BrowserDownloadUrl,
                DownloadSize = setupAsset?.Size ?? 0,
                Version = remoteVersion ?? new Version(0, 0, 0),
                IsNewerThanCurrent = isNewer
            };

            LatestRelease = info;

            if (isNewer && !string.IsNullOrEmpty(info.DownloadUrl))
            {
                SetState(UpdateState.UpdateAvailable, $"New version {info.TagName} is available!");

                if (silentBackground)
                {
                    ToastNotificationService.ShowToastWithBanner(
                        "LivelyScreen Update Available",
                        $"Version {info.TagName} is ready to install. Click to view release notes."
                    );
                }
            }
            else
            {
                SetState(UpdateState.UpToDate, $"You're up to date! LivelyScreen {CurrentVersionString} is the latest version.");
            }

            return info;
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"[UpdateService] Exception during check: {ex}");
            SetState(UpdateState.Failed, $"Check failed: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> DownloadAndInstallAsync(CancellationToken cancellationToken = default)
    {
        if (LatestRelease == null || string.IsNullOrEmpty(LatestRelease.DownloadUrl))
        {
            SetState(UpdateState.Failed, "No update download URL available.");
            return false;
        }

        if (State == UpdateState.Downloading)
            return false;

        SetState(UpdateState.Downloading, $"Downloading {LatestRelease.TagName} installer...");
        DownloadProgress = 0;
        DownloadProgressChanged?.Invoke(0);

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "LivelyScreenUpdates");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            var fileName = Path.GetFileName(new Uri(LatestRelease.DownloadUrl).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "LivelyScreen-Setup-win-x64.exe";
            }

            var installerPath = Path.Combine(tempDir, fileName);
            if (File.Exists(installerPath))
            {
                try { File.Delete(installerPath); } catch { }
            }

            using var response = await _httpClient.GetAsync(LatestRelease.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? LatestRelease.DownloadSize;
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fs = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fs.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalRead += bytesRead;
                if (totalBytes > 0)
                {
                    var progress = (double)totalRead / totalBytes;
                    DownloadProgress = progress;
                    _dispatcherQueue.TryEnqueue(() => DownloadProgressChanged?.Invoke(progress));
                }
            }

            DownloadedInstallerPath = installerPath;
            SetState(UpdateState.Downloaded, "Download complete. Installing update...");

            // Execute silent background installation handoff
            InstallSilentAndRestart(installerPath);
            return true;
        }
        catch (OperationCanceledException)
        {
            SetState(UpdateState.Idle, "Download cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"[UpdateService] Download error: {ex}");
            SetState(UpdateState.Failed, $"Download failed: {ex.Message}");
            return false;
        }
    }

    public static void InstallSilentAndRestart(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            LivelyScreen_WinUI.App.Log($"[UpdateService] Installer not found at {installerPath}");
            return;
        }

        try
        {
            LivelyScreen_WinUI.App.Log($"[UpdateService] Launching silent installer: {installerPath}");

            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true
            };

            Process.Start(psi);

            // Cleanly exit current instance so the installer can replace files without file locks
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"[UpdateService] Error launching installer: {ex.Message}");
        }
    }

    private void SetState(UpdateState state, string message)
    {
        State = state;
        StatusMessage = message;
        _dispatcherQueue.TryEnqueue(() => StateChanged?.Invoke(state, message));
    }

    public static Version? ParseVersion(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var clean = tag.Trim().TrimStart('v', 'V');
        if (Version.TryParse(clean, out var parsed))
        {
            return parsed;
        }

        var parts = clean.Split('.');
        if (parts.Length == 2 && Version.TryParse($"{clean}.0", out var twoPart))
        {
            return twoPart;
        }

        return null;
    }
}
