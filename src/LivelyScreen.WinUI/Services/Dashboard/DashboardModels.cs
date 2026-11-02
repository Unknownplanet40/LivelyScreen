namespace LivelyScreen.Services.Dashboard;

public interface ISystemInfoSource
{
    SystemInfoData GetSystemInfo();
}

public interface ICpuSource : IDisposable
{
    CpuSample Sample();
}

public interface IMemorySource
{
    MemorySample Sample();
}

public interface IStorageSource : IDisposable
{
    StorageSample Sample();
}

public interface INetworkSource : IDisposable
{
    NetworkSample Sample();
}

public interface IProcessSource
{
    ProcessSnapshot Sample();
}

public interface IConnectionSource
{
    ConnectionSnapshot Sample(IReadOnlyDictionary<int, ProcessIdentity> processMap);
}

public sealed record SystemInfoData
{
    public required string HostName { get; init; }
    public required string UserName { get; init; }
    public required string OsDisplayName { get; init; }
    public required string OsBuildString { get; init; }
    public required string CpuName { get; init; }
    public required int PhysicalCores { get; init; }
    public required int LogicalCores { get; init; }
    public required int BaseClockMhz { get; init; }
    public required ulong RamTotalBytes { get; init; }
    public required string RamType { get; init; }
    public required int RamSpeedMhz { get; init; }
    public required string GpuName { get; init; }
    public required ulong GpuVramBytes { get; init; }
    public required string PrimaryDiskSummary { get; init; }
    public required string BoardName { get; init; }
    public required string BiosVersionAndDate { get; init; }
    public required string DisplaySummary { get; init; }
}

public sealed record CpuSample
{
    public required double TotalPercent { get; init; }
    public required IReadOnlyList<double> PerCorePercent { get; init; }
    public double? TemperatureCelsius { get; init; }
}

public readonly record struct MemorySample
{
    public required ulong TotalBytes { get; init; }
    public required ulong UsedBytes { get; init; }
    public required double UsedPercent { get; init; }
    public ulong? CachedBytes { get; init; }
    public ulong? CommitTotalBytes { get; init; }
    public ulong? CommitLimitBytes { get; init; }
    public ulong? PagedPoolBytes { get; init; }
}

public readonly record struct DriveCapacitySample
{
    public required string Name { get; init; }
    public required ulong UsedBytes { get; init; }
    public required ulong TotalBytes { get; init; }
    public required double UsedPercent { get; init; }
}

public sealed record StorageSample
{
    public required IReadOnlyList<DriveCapacitySample> Drives { get; init; }
    public double? ReadBytesPerSec { get; init; }
    public double? WriteBytesPerSec { get; init; }
}

public sealed record NetworkSample
{
    public required bool IsAvailable { get; init; }
    public required string AdapterName { get; init; }
    public required string AdapterKindLabel { get; init; }
    public required string Ipv4Address { get; init; }
    public required string MacAddress { get; init; }
    public required string GatewayAddress { get; init; }
    public required long LinkSpeedMbps { get; init; }
    public int? WifiRssiDbm { get; init; }
    public required double RxBytesPerSec { get; init; }
    public required double TxBytesPerSec { get; init; }
}

public readonly record struct ProcessIdentity(int Pid, string Name, int ParentPid);

public readonly record struct ProcessSample
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public required double CpuPercent { get; init; }
    public required ulong WorkingSetBytes { get; init; }
}

public sealed record ProcessSnapshot
{
    public required IReadOnlyList<ProcessSample> TopProcesses { get; init; }
    public required IReadOnlyDictionary<int, ProcessIdentity> AllProcesses { get; init; }
}

public readonly record struct ConnectionKey(
    string Protocol,
    string LocalEndpoint,
    string RemoteEndpoint,
    int OwningPid);

public readonly record struct ConnectionSample
{
    public required string Protocol { get; init; }
    public required string RemoteEndpoint { get; init; }
    public required string State { get; init; }
    public required string ProcessName { get; init; }
    public required int OwningPid { get; init; }
}

public sealed record ConnectionSnapshot
{
    public required int EstablishedCount { get; init; }
    public required int ListenCount { get; init; }
    public required IReadOnlyList<ConnectionSample> ActiveRows { get; init; }
    public required IReadOnlyDictionary<ConnectionKey, ConnectionSample> EstablishedConnections { get; init; }
}

public enum DashboardEventCategory
{
    NET,
    PROC,
    DISK,
    SYS
}

public enum DashboardEventSeverity
{
    Normal,
    Warning,
    Critical,
    Dim
}

public readonly record struct DashboardEventEntry
{
    public required DateTime Timestamp { get; init; }
    public required DashboardEventCategory Category { get; init; }
    public required DashboardEventSeverity Severity { get; init; }
    public required string Message { get; init; }
}

public sealed record DashboardSnapshot
{
    public required DateTime TimestampLocal { get; init; }
    public required TimeSpan Uptime { get; init; }
    public required string? BatteryStatus { get; init; }
    public required SystemInfoData SystemInfo { get; init; }
    public required CpuSample Cpu { get; init; }
    public required MemorySample Memory { get; init; }
    public required StorageSample Storage { get; init; }
    public required NetworkSample Network { get; init; }
    public required ProcessSnapshot Processes { get; init; }
    public required ConnectionSnapshot Connections { get; init; }
    public required IReadOnlyList<DashboardEventEntry> NewEvents { get; init; }
}
