using System.Globalization;

namespace LivelyScreen.Services.Dashboard;

public sealed class DashboardEventDiffBuilder
{
    public const double DiskBurstThresholdBytesPerSec = 25.0 * 1024.0 * 1024.0; // 25 MB/s
    public const double CpuSustainedThresholdPercent = 80.0;
    public const double CpuCoreSpikeThresholdPercent = 90.0;
    public const double CpuTempWarningCelsius = 75.0;
    public const double CpuTempCriticalCelsius = 85.0;

    private double _writeBurstDurationSec;
    private double _writeBurstPeakBytesPerSec;
    private bool _writeBurstLogged;

    private double _readBurstDurationSec;
    private double _readBurstPeakBytesPerSec;
    private bool _readBurstLogged;

    private double _cpuSustainedDurationSec;
    private DateTime _lastCpuEventTime = DateTime.MinValue;
    private DateTime _lastTempEventTime = DateTime.MinValue;

    public IReadOnlyList<DashboardEventEntry> BuildDiffEvents(
        DashboardSnapshot? previous,
        DateTime nowLocal,
        SystemInfoData systemInfo,
        CpuSample cpu,
        StorageSample storage,
        NetworkSample network,
        ProcessSnapshot processes,
        ConnectionSnapshot connections,
        IReadOnlyList<string>? pendingNetworkChangeNotes = null,
        double elapsedSeconds = 1.0)
    {
        if (elapsedSeconds <= 0.0 || double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds))
            elapsedSeconds = 1.0;

        var events = new List<DashboardEventEntry>();

        // 1. NetworkChange notifications from OS
        if (pendingNetworkChangeNotes != null)
        {
            foreach (var note in pendingNetworkChangeNotes)
            {
                if (!string.IsNullOrWhiteSpace(note))
                {
                    events.Add(new DashboardEventEntry
                    {
                        Timestamp = nowLocal,
                        Category = DashboardEventCategory.NET,
                        Severity = DashboardEventSeverity.Normal,
                        Message = note
                    });
                }
            }
        }

        if (previous == null)
        {
            // Initial baseline snapshot: no fake events, only real diff events on subsequent ticks.
            return events;
        }

        // 2. Network adapter state / IPv4 changes between snapshots
        if (previous.Network.IsAvailable != network.IsAvailable)
        {
            events.Add(new DashboardEventEntry
            {
                Timestamp = nowLocal,
                Category = DashboardEventCategory.NET,
                Severity = network.IsAvailable ? DashboardEventSeverity.Normal : DashboardEventSeverity.Warning,
                Message = network.IsAvailable
                    ? $"adapter up  {network.AdapterName}  {network.Ipv4Address}"
                    : $"adapter down  {previous.Network.AdapterName}"
            });
        }
        else if (network.IsAvailable &&
                 !string.Equals(previous.Network.Ipv4Address, network.Ipv4Address, StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(network.Ipv4Address))
        {
            events.Add(new DashboardEventEntry
            {
                Timestamp = nowLocal,
                Category = DashboardEventCategory.NET,
                Severity = DashboardEventSeverity.Normal,
                Message = $"address change  {network.AdapterName} -> {network.Ipv4Address}"
            });
        }

        // 3. New or closed ESTABLISHED TCP connections (cap per tick so a browser burst doesn't flood)
        int connEventsThisTick = 0;
        foreach (var kvp in connections.EstablishedConnections)
        {
            if (connEventsThisTick >= 3) break;
            if (!previous.Connections.EstablishedConnections.ContainsKey(kvp.Key))
            {
                var c = kvp.Value;
                events.Add(new DashboardEventEntry
                {
                    Timestamp = nowLocal,
                    Category = DashboardEventCategory.NET,
                    Severity = DashboardEventSeverity.Normal,
                    Message = $"tcp connect  {c.ProcessName} -> {c.RemoteEndpoint}"
                });
                connEventsThisTick++;
            }
        }

        foreach (var kvp in previous.Connections.EstablishedConnections)
        {
            if (connEventsThisTick >= 4) break;
            if (!connections.EstablishedConnections.ContainsKey(kvp.Key))
            {
                var c = kvp.Value;
                events.Add(new DashboardEventEntry
                {
                    Timestamp = nowLocal,
                    Category = DashboardEventCategory.NET,
                    Severity = DashboardEventSeverity.Dim,
                    Message = $"tcp close  {c.ProcessName} -> {c.RemoteEndpoint}"
                });
                connEventsThisTick++;
            }
        }

        // 4. Process spawns and exits by diffing PID maps
        int procEventsThisTick = 0;
        foreach (var kvp in processes.AllProcesses)
        {
            if (procEventsThisTick >= 3) break;
            if (!previous.Processes.AllProcesses.ContainsKey(kvp.Key))
            {
                var proc = kvp.Value;
                string parentSuffix = string.Empty;
                if (proc.ParentPid > 0 &&
                    (processes.AllProcesses.TryGetValue(proc.ParentPid, out var parentProc) ||
                     previous.Processes.AllProcesses.TryGetValue(proc.ParentPid, out parentProc)) &&
                    !string.IsNullOrWhiteSpace(parentProc.Name))
                {
                    parentSuffix = $"  parent {parentProc.Name}";
                }

                events.Add(new DashboardEventEntry
                {
                    Timestamp = nowLocal,
                    Category = DashboardEventCategory.PROC,
                    Severity = DashboardEventSeverity.Normal,
                    Message = $"spawn  {proc.Name}  pid {proc.Pid}{parentSuffix}"
                });
                procEventsThisTick++;
            }
        }

        foreach (var kvp in previous.Processes.AllProcesses)
        {
            if (procEventsThisTick >= 5) break;
            if (!processes.AllProcesses.ContainsKey(kvp.Key))
            {
                var proc = kvp.Value;
                events.Add(new DashboardEventEntry
                {
                    Timestamp = nowLocal,
                    Category = DashboardEventCategory.PROC,
                    Severity = DashboardEventSeverity.Dim,
                    Message = $"exit  {proc.Name}  pid {proc.Pid}"
                });
                procEventsThisTick++;
            }
        }

        // 5. Disk throughput bursts (>= threshold for >= 1.0 s)
        string primaryDriveLabel = storage.Drives.Count > 0 ? storage.Drives[0].Name : "C:";
        double writeBps = storage.WriteBytesPerSec ?? 0.0;
        if (writeBps >= DiskBurstThresholdBytesPerSec)
        {
            _writeBurstDurationSec += elapsedSeconds;
            if (writeBps > _writeBurstPeakBytesPerSec)
                _writeBurstPeakBytesPerSec = writeBps;

            if (_writeBurstDurationSec >= 1.0 && !_writeBurstLogged)
            {
                _writeBurstLogged = true;
                events.Add(new DashboardEventEntry
                {
                    Timestamp = nowLocal,
                    Category = DashboardEventCategory.DISK,
                    Severity = DashboardEventSeverity.Warning,
                    Message = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}  write burst {1} ({2:F1} s)",
                        primaryDriveLabel,
                        DashboardFormatters.FormatRateBytesPerSec(_writeBurstPeakBytesPerSec),
                        _writeBurstDurationSec)
                });
            }
        }
        else
        {
            _writeBurstDurationSec = 0.0;
            _writeBurstPeakBytesPerSec = 0.0;
            _writeBurstLogged = false;
        }

        double readBps = storage.ReadBytesPerSec ?? 0.0;
        if (readBps >= DiskBurstThresholdBytesPerSec)
        {
            _readBurstDurationSec += elapsedSeconds;
            if (readBps > _readBurstPeakBytesPerSec)
                _readBurstPeakBytesPerSec = readBps;

            if (_readBurstDurationSec >= 1.0 && !_readBurstLogged)
            {
                _readBurstLogged = true;
                events.Add(new DashboardEventEntry
                {
                    Timestamp = nowLocal,
                    Category = DashboardEventCategory.DISK,
                    Severity = DashboardEventSeverity.Warning,
                    Message = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}  read burst {1} ({2:F1} s)",
                        primaryDriveLabel,
                        DashboardFormatters.FormatRateBytesPerSec(_readBurstPeakBytesPerSec),
                        _readBurstDurationSec)
                });
            }
        }
        else
        {
            _readBurstDurationSec = 0.0;
            _readBurstPeakBytesPerSec = 0.0;
            _readBurstLogged = false;
        }

        // 6. CPU sustained load or core spike & temperature thresholds
        if (cpu.TotalPercent >= CpuSustainedThresholdPercent)
        {
            _cpuSustainedDurationSec += elapsedSeconds;
        }
        else
        {
            _cpuSustainedDurationSec = 0.0;
        }

        if ((nowLocal - _lastCpuEventTime).TotalSeconds >= 8.0)
        {
            int hottestCore = -1;
            double hottestLoad = 0.0;
            for (int i = 0; i < cpu.PerCorePercent.Count; i++)
            {
                if (cpu.PerCorePercent[i] > hottestLoad)
                {
                    hottestLoad = cpu.PerCorePercent[i];
                    hottestCore = i;
                }
            }

            if (_cpuSustainedDurationSec >= 2.0 || hottestLoad >= CpuCoreSpikeThresholdPercent)
            {
                _lastCpuEventTime = nowLocal;
                string tempTag = cpu.TemperatureCelsius.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, "  temp {0:F0}C", cpu.TemperatureCelsius.Value)
                    : string.Empty;
                string msg = hottestCore >= 0 && hottestLoad >= CpuCoreSpikeThresholdPercent
                    ? string.Format(CultureInfo.InvariantCulture, "cpu core {0} load {1:F0}%  total {2:F0}%{3}", hottestCore, hottestLoad, cpu.TotalPercent, tempTag)
                    : string.Format(CultureInfo.InvariantCulture, "cpu sustained load {0:F0}% ({1:F1} s){2}", cpu.TotalPercent, _cpuSustainedDurationSec, tempTag);

                events.Add(new DashboardEventEntry
                {
                    Timestamp = nowLocal,
                    Category = DashboardEventCategory.SYS,
                    Severity = cpu.TotalPercent >= 90.0 ? DashboardEventSeverity.Critical : DashboardEventSeverity.Dim,
                    Message = msg
                });
            }
        }

        if (cpu.TemperatureCelsius.HasValue &&
            cpu.TemperatureCelsius.Value >= CpuTempWarningCelsius &&
            (nowLocal - _lastTempEventTime).TotalSeconds >= 15.0)
        {
            _lastTempEventTime = nowLocal;
            double tempC = cpu.TemperatureCelsius.Value;
            events.Add(new DashboardEventEntry
            {
                Timestamp = nowLocal,
                Category = DashboardEventCategory.SYS,
                Severity = tempC >= CpuTempCriticalCelsius ? DashboardEventSeverity.Critical : DashboardEventSeverity.Warning,
                Message = string.Format(CultureInfo.InvariantCulture, "thermal zone temperature {0:F0}C", tempC)
            });
        }

        return events;
    }
}
