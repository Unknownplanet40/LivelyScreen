using System.Diagnostics;
using System.Net.NetworkInformation;

namespace LivelyScreen.Services.Dashboard;

public sealed class DashboardSampler : IDisposable
{
    public static DashboardSampler Shared { get; } = new(
        new SystemInfoSource(),
        new CpuSource(),
        new MemorySource(),
        new StorageSource(),
        new NetworkSource(),
        new ProcessSource(),
        new ConnectionSource());

    private readonly ISystemInfoSource _systemInfoSource;
    private readonly ICpuSource _cpuSource;
    private readonly IMemorySource _memorySource;
    private readonly IStorageSource _storageSource;
    private readonly INetworkSource _networkSource;
    private readonly IProcessSource _processSource;
    private readonly IConnectionSource _connectionSource;
    private readonly DashboardEventDiffBuilder _diffBuilder = new();

    private readonly object _gate = new();
    private readonly List<string> _pendingNetworkNotes = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private int _subscriberCount;
    private DashboardSnapshot? _latestSnapshot;
    private long _lastTickTimestamp;

    public DashboardSnapshot? LatestSnapshot => Volatile.Read(ref _latestSnapshot);

    public event Action<DashboardSnapshot>? SnapshotPublished;

    public DashboardSampler(
        ISystemInfoSource systemInfoSource,
        ICpuSource cpuSource,
        IMemorySource memorySource,
        IStorageSource storageSource,
        INetworkSource networkSource,
        IProcessSource processSource,
        IConnectionSource connectionSource)
    {
        _systemInfoSource = systemInfoSource;
        _cpuSource = cpuSource;
        _memorySource = memorySource;
        _storageSource = storageSource;
        _networkSource = networkSource;
        _processSource = processSource;
        _connectionSource = connectionSource;
    }

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

        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _loopTask = Task.Run(() => RunLoopAsync(token), token);
    }

    private void StopInternal()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

        var cts = _cts;
        _cts = null;
        if (cts != null)
        {
            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
        }
        _loopTask = null;
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        lock (_pendingNetworkNotes)
        {
            _pendingNetworkNotes.Add(e.IsAvailable ? "network availability -> online" : "network availability -> offline");
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        lock (_pendingNetworkNotes)
        {
            _pendingNetworkNotes.Add("network interface address table updated");
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            // Immediate initial sample on background thread
            PublishSingleTick();

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                PublishSingleTick();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"DashboardSampler loop error: {ex.Message}");
        }
    }

    public DashboardSnapshot SampleOnce()
    {
        long nowTicks = Stopwatch.GetTimestamp();
        double elapsedSec = _lastTickTimestamp > 0
            ? (nowTicks - _lastTickTimestamp) / (double)Stopwatch.Frequency
            : 1.0;
        _lastTickTimestamp = nowTicks;

        DateTime nowLocal = DateTime.Now;
        TimeSpan uptime = PowerAndUptimeHelper.GetSystemUptime();
        string? battery = PowerAndUptimeHelper.QueryBatteryStatusText();
        SystemInfoData sysInfo = _systemInfoSource.GetSystemInfo();
        CpuSample cpu = _cpuSource.Sample();
        MemorySample memory = _memorySource.Sample();
        StorageSample storage = _storageSource.Sample();
        NetworkSample network = _networkSource.Sample();
        ProcessSnapshot processes = _processSource.Sample();
        ConnectionSnapshot connections = _connectionSource.Sample(processes.AllProcesses);

        List<string>? netNotes = null;
        lock (_pendingNetworkNotes)
        {
            if (_pendingNetworkNotes.Count > 0)
            {
                netNotes = new List<string>(_pendingNetworkNotes);
                _pendingNetworkNotes.Clear();
            }
        }

        DashboardSnapshot? prev = Volatile.Read(ref _latestSnapshot);
        IReadOnlyList<DashboardEventEntry> diffEvents = _diffBuilder.BuildDiffEvents(
            prev,
            nowLocal,
            sysInfo,
            cpu,
            storage,
            network,
            processes,
            connections,
            netNotes,
            elapsedSec);

        var snapshot = new DashboardSnapshot
        {
            TimestampLocal = nowLocal,
            Uptime = uptime,
            BatteryStatus = battery,
            SystemInfo = sysInfo,
            Cpu = cpu,
            Memory = memory,
            Storage = storage,
            Network = network,
            Processes = processes,
            Connections = connections,
            NewEvents = diffEvents
        };

        Volatile.Write(ref _latestSnapshot, snapshot);
        return snapshot;
    }

    private void PublishSingleTick()
    {
        var snapshot = SampleOnce();
        SnapshotPublished?.Invoke(snapshot);
    }

    public void Dispose()
    {
        StopInternal();
        _cpuSource.Dispose();
        _storageSource.Dispose();
        _networkSource.Dispose();
    }
}
