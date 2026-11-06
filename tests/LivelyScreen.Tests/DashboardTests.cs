using System;
using System.Collections.Generic;
using LivelyScreen.Services.Dashboard;
using Xunit;

namespace LivelyScreen.Tests;

public sealed class DashboardTests
{
    [Theory]
    [InlineData(-5.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(42.5, 42.5)]
    [InlineData(105.2, 100.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    public void ClampPercent_ClampsAndHandlesNaN(double input, double expected)
    {
        Assert.Equal(expected, DashboardFormatters.ClampPercent(input));
    }

    [Fact]
    public void ComputeCounterRate_HandlesCounterResetAndZeroElapsed()
    {
        // Normal increment: 10,000,000 bytes over 2 seconds = 5,000,000 B/s
        Assert.Equal(5_000_000d, DashboardFormatters.ComputeCounterRate(10_000_000UL, 20_000_000UL, 2.0));

        // Counter reset (current < previous) -> 0, never negative
        Assert.Equal(0d, DashboardFormatters.ComputeCounterRate(20_000_000UL, 5_000_000UL, 1.0));

        // Zero or negative elapsed -> 0
        Assert.Equal(0d, DashboardFormatters.ComputeCounterRate(100UL, 200UL, 0.0));
        Assert.Equal(0d, DashboardFormatters.ComputeCounterRate(100UL, 200UL, -1.0));

        // Signed counter rate
        Assert.Equal(0d, DashboardFormatters.ComputeSignedCounterRate(5000L, 1000L, 1.0));
        Assert.Equal(1000d, DashboardFormatters.ComputeSignedCounterRate(1000L, 3000L, 2.0));
    }

    [Fact]
    public void FormatBytesAndRates_FormatCleanly()
    {
        Assert.Equal("16.0 GB", DashboardFormatters.FormatGigabytes((ulong)(16.0 * 1024 * 1024 * 1024), 1));
        Assert.Equal("1284 MB", DashboardFormatters.FormatMegabytes(1284UL * 1024 * 1024));
        Assert.Equal("338/476 GB", DashboardFormatters.FormatDriveUsage(338UL * 1024 * 1024 * 1024, 476UL * 1024 * 1024 * 1024));
        Assert.Equal("0 B/s", DashboardFormatters.FormatRateBytesPerSec(0));
        Assert.Equal("412 KB/s", DashboardFormatters.FormatRateBytesPerSec(412 * 1024.0));
        Assert.Equal("2.84 MB/s", DashboardFormatters.FormatRateBytesPerSec(2.84 * 1024 * 1024));
        Assert.Equal("18.4 MB/s", DashboardFormatters.FormatRateBytesPerSec(18.4 * 1024 * 1024));
    }

    [Fact]
    public void FormatUptime_FormatsDaysHoursMinutesSeconds()
    {
        var uptimeWithDays = new TimeSpan(3, 4, 17, 52);
        Assert.Equal("uptime 3d 04:17:52", DashboardFormatters.FormatUptime(uptimeWithDays));

        var uptimeNoDays = new TimeSpan(0, 12, 5, 9);
        Assert.Equal("uptime 0d 12:05:09", DashboardFormatters.FormatUptime(uptimeNoDays));
    }

    [Fact]
    public void MaskSensitive_MasksHostUserIpAndMacWhenEnabled()
    {
        Assert.Equal("DESKTOP-K7Q2", DashboardFormatters.MaskHostname("DESKTOP-K7Q2", maskSensitive: false));
        Assert.Equal("DESKTOP-****", DashboardFormatters.MaskHostname("DESKTOP-K7Q2", maskSensitive: true));

        Assert.Equal("caps", DashboardFormatters.MaskUsername("caps", maskSensitive: false));
        Assert.Equal("****", DashboardFormatters.MaskUsername("caps", maskSensitive: true));

        Assert.Equal("192.168.1.14", DashboardFormatters.MaskIpv4OrEndpoint("192.168.1.14", maskSensitive: false));
        Assert.Equal("192.168.x.x", DashboardFormatters.MaskIpv4OrEndpoint("192.168.1.14", maskSensitive: true));

        Assert.Equal("142.250.190.78:443", DashboardFormatters.MaskIpv4OrEndpoint("142.250.190.78:443", maskSensitive: false));
        Assert.Equal("142.250.x.x:443", DashboardFormatters.MaskIpv4OrEndpoint("142.250.190.78:443", maskSensitive: true));

        Assert.Equal("AA:BB:CC:11:22:33", DashboardFormatters.MaskMacAddress("AA:BB:CC:11:22:33", maskSensitive: false));
        Assert.Equal("AA:BB:CC:xx:xx:xx", DashboardFormatters.MaskMacAddress("AA:BB:CC:11:22:33", maskSensitive: true));
    }

    [Fact]
    public void RingBuffer_MaintainsBoundedCapacityAndChronologicalOrder()
    {
        var rb = new RingBuffer<int>(4);
        Assert.Equal(0, rb.Count);
        Assert.Equal(4, rb.Capacity);

        rb.Add(10);
        rb.Add(20);
        rb.Add(30);
        Assert.Equal(3, rb.Count);
        Assert.Equal(10, rb[0]);
        Assert.Equal(30, rb[2]);

        rb.Add(40);
        rb.Add(50);
        rb.Add(60);

        Assert.Equal(4, rb.Count);
        Assert.Equal(30, rb[0]);
        Assert.Equal(40, rb[1]);
        Assert.Equal(50, rb[2]);
        Assert.Equal(60, rb[3]);

        Span<int> copy = stackalloc int[4];
        rb.CopyTo(copy);
        Assert.Equal(new[] { 30, 40, 50, 60 }, copy.ToArray());

        rb.Clear();
        Assert.Equal(0, rb.Count);
    }

    [Fact]
    public void EventDiffBuilder_EmitsRealDiffEventsWithoutInventedNoise()
    {
        var builder = new DashboardEventDiffBuilder();
        var t0 = new DateTime(2026, 11, 2, 14, 32, 0);

        var sysInfo = new FakeSystemInfoSource().GetSystemInfo();
        var cpu0 = new CpuSample
        {
            TotalPercent = 15.0,
            PerCorePercent = [15.0, 15.0],
            TemperatureCelsius = 52.0
        };
        var mem0 = new FakeMemorySource().Sample();
        var storage0 = new StorageSample
        {
            Drives = [new DriveCapacitySample { Name = "C:", UsedBytes = 300_000_000_000UL, TotalBytes = 500_000_000_000UL, UsedPercent = 60.0 }],
            ReadBytesPerSec = 0.0,
            WriteBytesPerSec = 0.0
        };
        var net0 = new NetworkSample
        {
            IsAvailable = true,
            AdapterName = "Intel AX201",
            AdapterKindLabel = "Wi-Fi",
            Ipv4Address = "192.168.1.14",
            MacAddress = "AA:BB:CC:DD:EE:FF",
            GatewayAddress = "192.168.1.1",
            LinkSpeedMbps = 866,
            WifiRssiDbm = -52,
            RxBytesPerSec = 1024,
            TxBytesPerSec = 512
        };
        var procs0 = new ProcessSnapshot
        {
            TopProcesses = [new ProcessSample { Pid = 100, Name = "explorer.exe", CpuPercent = 1.0, WorkingSetBytes = 100_000_000UL }],
            AllProcesses = new Dictionary<int, ProcessIdentity>
            {
                [100] = new ProcessIdentity(100, "explorer.exe", 4)
            }
        };
        var key0 = new ConnectionKey("TCP", "192.168.1.14:51000", "142.250.190.78:443", 100);
        var connSample0 = new ConnectionSample
        {
            Protocol = "TCP",
            RemoteEndpoint = "142.250.190.78:443",
            State = "ESTAB",
            ProcessName = "explorer.exe",
            OwningPid = 100
        };
        var conns0 = new ConnectionSnapshot
        {
            EstablishedCount = 1,
            ListenCount = 0,
            ActiveRows = [connSample0],
            EstablishedConnections = new Dictionary<ConnectionKey, ConnectionSample> { [key0] = connSample0 }
        };

        // First sample establishes baseline -> no synthetic events
        var initialEvents = builder.BuildDiffEvents(null, t0, sysInfo, cpu0, storage0, net0, procs0, conns0, null, 1.0);
        Assert.Empty(initialEvents);

        var snap0 = new DashboardSnapshot
        {
            TimestampLocal = t0,
            Uptime = TimeSpan.FromHours(1),
            BatteryStatus = "AC power",
            SystemInfo = sysInfo,
            Cpu = cpu0,
            Memory = mem0,
            Storage = storage0,
            Network = net0,
            Processes = procs0,
            Connections = conns0,
            NewEvents = initialEvents
        };

        // Second sample: Spotify spawns, Spotify connects TCP, disk bursts 82 MB/s write for 1.2s
        var t1 = t0.AddSeconds(1.2);
        var procs1 = new ProcessSnapshot
        {
            TopProcesses = procs0.TopProcesses,
            AllProcesses = new Dictionary<int, ProcessIdentity>(procs0.AllProcesses)
            {
                [3316] = new ProcessIdentity(3316, "Spotify.exe", 100)
            }
        };
        var key1 = new ConnectionKey("TCP", "192.168.1.14:51001", "52.109.12.21:443", 3316);
        var connSample1 = new ConnectionSample
        {
            Protocol = "TCP",
            RemoteEndpoint = "52.109.12.21:443",
            State = "ESTAB",
            ProcessName = "Spotify.exe",
            OwningPid = 3316
        };
        var conns1 = new ConnectionSnapshot
        {
            EstablishedCount = 2,
            ListenCount = 0,
            ActiveRows = [connSample0, connSample1],
            EstablishedConnections = new Dictionary<ConnectionKey, ConnectionSample>(conns0.EstablishedConnections)
            {
                [key1] = connSample1
            }
        };
        var storage1 = new StorageSample
        {
            Drives = storage0.Drives,
            ReadBytesPerSec = 0.0,
            WriteBytesPerSec = 82.0 * 1024.0 * 1024.0 // 82 MB/s for 1.2s
        };

        var events1 = builder.BuildDiffEvents(snap0, t1, sysInfo, cpu0, storage1, net0, procs1, conns1, null, 1.2);
        Assert.Contains(events1, e => e.Category == DashboardEventCategory.PROC && e.Message.Contains("spawn  Spotify.exe  pid 3316  parent explorer.exe"));
        Assert.Contains(events1, e => e.Category == DashboardEventCategory.NET && e.Message.Contains("tcp connect  Spotify.exe -> 52.109.12.21:443"));
        Assert.Contains(events1, e => e.Category == DashboardEventCategory.DISK && e.Message.Contains("write burst"));
    }

    [Fact]
    public void FakeSources_HandleMissingOptionalFieldsGracefully()
    {
        ISystemInfoSource sys = new FakeSystemInfoSource();
        using ICpuSource cpu = new FakeCpuSource();
        IMemorySource mem = new FakeMemorySource();
        using IStorageSource storage = new FakeStorageSource();
        using INetworkSource net = new FakeNetworkSource();
        IProcessSource proc = new FakeProcessSource();
        IConnectionSource conn = new FakeConnectionSource();

        var staticInfo = sys.GetSystemInfo();
        var cpuMetrics = cpu.Sample();
        var memMetrics = mem.Sample();
        var storageMetrics = storage.Sample();
        var netMetrics = net.Sample();
        var procSnap = proc.Sample();
        var connSnap = conn.Sample(procSnap.AllProcesses);

        // Verify optional fields (TemperatureCelsius, WifiRssiDbm, BoardName) can be null/empty without throwing
        Assert.Null(cpuMetrics.TemperatureCelsius);
        Assert.Null(netMetrics.WifiRssiDbm);
        Assert.Empty(staticInfo.BoardName);
        Assert.InRange(memMetrics.UsedPercent, 0.0, 100.0);
        Assert.Single(storageMetrics.Drives);
        Assert.Single(procSnap.TopProcesses);
        Assert.Single(connSnap.ActiveRows);
    }

    private sealed class FakeSystemInfoSource : ISystemInfoSource
    {
        public SystemInfoData GetSystemInfo() => new()
        {
            HostName = "TEST-HOST",
            UserName = "tester",
            OsDisplayName = "Windows 11 Pro 24H2",
            OsBuildString = "26100.1",
            CpuName = "Test CPU",
            PhysicalCores = 6,
            LogicalCores = 12,
            BaseClockMhz = 2500,
            RamTotalBytes = 16UL * 1024 * 1024 * 1024,
            RamType = "DDR4",
            RamSpeedMhz = 3200,
            GpuName = "Test GPU",
            GpuVramBytes = 8UL * 1024 * 1024 * 1024,
            PrimaryDiskSummary = "C: 476 GB  71% used",
            BoardName = string.Empty,
            BiosVersionAndDate = "1.0 2025-01-01",
            DisplaySummary = "1920x1080 @60 Hz"
        };
    }

    private sealed class FakeCpuSource : ICpuSource
    {
        public CpuSample Sample() => new()
        {
            TotalPercent = 24.5,
            PerCorePercent = new double[12],
            TemperatureCelsius = null
        };

        public void Dispose() { }
    }

    private sealed class FakeMemorySource : IMemorySource
    {
        public MemorySample Sample() => new()
        {
            TotalBytes = 16UL * 1024 * 1024 * 1024,
            UsedBytes = 8UL * 1024 * 1024 * 1024,
            UsedPercent = 50.0,
            CachedBytes = 2UL * 1024 * 1024 * 1024,
            CommitTotalBytes = 10UL * 1024 * 1024 * 1024,
            CommitLimitBytes = 20UL * 1024 * 1024 * 1024,
            PagedPoolBytes = 256UL * 1024 * 1024
        };
    }

    private sealed class FakeStorageSource : IStorageSource
    {
        public StorageSample Sample() => new()
        {
            Drives = [new DriveCapacitySample { Name = "C:", UsedBytes = 200UL * 1024 * 1024 * 1024, TotalBytes = 500UL * 1024 * 1024 * 1024, UsedPercent = 40.0 }],
            ReadBytesPerSec = 1024,
            WriteBytesPerSec = 2048
        };

        public void Dispose() { }
    }

    private sealed class FakeNetworkSource : INetworkSource
    {
        public NetworkSample Sample() => new()
        {
            IsAvailable = true,
            AdapterName = "Intel I225-V",
            AdapterKindLabel = "Ethernet",
            Ipv4Address = "10.0.0.5",
            MacAddress = "00:11:22:33:44:55",
            GatewayAddress = "10.0.0.1",
            LinkSpeedMbps = 1000,
            WifiRssiDbm = null,
            RxBytesPerSec = 4096,
            TxBytesPerSec = 1024
        };

        public void Dispose() { }
    }

    private sealed class FakeProcessSource : IProcessSource
    {
        public ProcessSnapshot Sample()
        {
            var entry = new ProcessSample
            {
                Pid = 1234,
                Name = "app.exe",
                CpuPercent = 4.2,
                WorkingSetBytes = 128UL * 1024 * 1024
            };
            return new ProcessSnapshot
            {
                TopProcesses = [entry],
                AllProcesses = new Dictionary<int, ProcessIdentity>
                {
                    [1234] = new ProcessIdentity(1234, "app.exe", 4)
                }
            };
        }
    }

    private sealed class FakeConnectionSource : IConnectionSource
    {
        public ConnectionSnapshot Sample(IReadOnlyDictionary<int, ProcessIdentity> processMap)
        {
            var conn = new ConnectionSample
            {
                Protocol = "TCP",
                RemoteEndpoint = "1.1.1.1:443",
                State = "ESTAB",
                ProcessName = "app.exe",
                OwningPid = 1234
            };
            var key = new ConnectionKey("TCP", "10.0.0.5:50000", "1.1.1.1:443", 1234);
            return new ConnectionSnapshot
            {
                EstablishedCount = 1,
                ListenCount = 0,
                ActiveRows = [conn],
                EstablishedConnections = new Dictionary<ConnectionKey, ConnectionSample>
                {
                    [key] = conn
                }
            };
        }
    }

    [Fact]
    public void MaskPath_MasksUserProfileAndUserName()
    {
        string userPath = @"C:\Users\tester\Documents\secret.txt";
        Assert.Equal(userPath, DashboardFormatters.MaskPath(userPath, maskSensitive: false));

        string currentUsername = Environment.UserName;
        string pathWithUser = $@"C:\Users\{currentUsername}\AppData\Local\Temp";
        string masked = DashboardFormatters.MaskPath(pathWithUser, maskSensitive: true);
        Assert.DoesNotContain(currentUsername, masked, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("****", masked);
    }

    [Fact]
    public void FormatHexLineBytes_Formats16BytesWithMiddleSpace()
    {
        byte[] bytes = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00];
        string hex = DashboardFormatters.FormatHexLineBytes(bytes);

        // 49 chars length: 16 pairs + 15 spaces + 1 middle extra space = 48 + 1 = 49
        Assert.Equal(49, hex.Length);
        Assert.StartsWith("4D 5A 90 00 03 00 00 00  04 00 00 00 FF FF 00 00", hex);

        string ascii = DashboardFormatters.FormatAsciiGutter(bytes);
        Assert.Equal(16, ascii.Length);
        Assert.StartsWith("MZ", ascii);
        Assert.Equal('.', ascii[2]); // 0x90 non-printable -> '.'
    }

    [Fact]
    public void FileReconSampler_CyclesModeAndMaintainsBuffer()
    {
        var sampler = FileReconSampler.Shared;
        var snapInitial = sampler.GetSnapshot(maskSensitive: false);
        Assert.NotNull(snapInitial);

        var initialMode = snapInitial.Mode;

        // Advance past 12.0s
        sampler.AdvanceTime(13.0);
        var snapAfter = sampler.GetSnapshot(maskSensitive: false);
        Assert.NotEqual(initialMode, snapAfter.Mode);

        // Advance again -> toggles back
        sampler.AdvanceTime(13.0);
        var snapBack = sampler.GetSnapshot(maskSensitive: false);
        Assert.Equal(initialMode, snapBack.Mode);
    }
}
