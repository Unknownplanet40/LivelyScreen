using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.NetworkManagement.IpHelper;
using Windows.Win32.NetworkManagement.WiFi;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Performance;
using Windows.Win32.System.Power;
using Windows.Win32.System.ProcessStatus;
using Windows.Win32.System.SystemInformation;
using Windows.Win32.System.WindowsProgramming;
using Windows.Wdk.System.SystemInformation;
using CsWin32PInvoke = Windows.Win32.CsWin32PInvoke;

namespace LivelyScreen.Services.Dashboard;

public sealed class SystemInfoSource : ISystemInfoSource
{
    private SystemInfoData? _cached;
    private readonly object _gate = new();

    public SystemInfoData GetSystemInfo()
    {
        lock (_gate)
        {
            if (_cached != null)
                return _cached;

            _cached = QuerySystemInfoOnce();
            return _cached;
        }
    }

    private static SystemInfoData QuerySystemInfoOnce()
    {
        string hostName = TryGet(() => Environment.MachineName, string.Empty);
        string userName = TryGet(() => Environment.UserName, string.Empty);

        // 1. OS Name + DisplayVersion + Build.UBR from Registry
        (string osDisplayName, string osBuildString) = ReadOsRegistryInfo();

        // 2. CPU Name & Base MHz from Registry + Core counts
        (string cpuName, int baseMhz) = ReadCpuRegistryInfo();
        (int physicalCores, int logicalCores) = QueryCoreCounts();

        // 3. Total Physical Memory + WMI RAM Type & Speed (queried once)
        ulong ramTotalBytes = QueryTotalPhysicalMemory();
        (string ramType, int ramSpeedMhz) = QueryRamWmiOnce();

        // 4. GPU Name & Dedicated VRAM via DXGI
        (string gpuName, ulong gpuVramBytes) = QueryPrimaryGpuDxgi();

        // 5. Primary Disk Summary
        string primaryDiskSummary = QueryPrimaryDiskSummary();

        // 6. Motherboard & BIOS from Registry
        (string boardName, string biosVersionAndDate) = ReadBiosRegistryInfo();

        // 7. Display Resolution & Refresh Rate via EnumDisplaySettingsW
        string displaySummary = QueryPrimaryDisplaySettings();

        return new SystemInfoData
        {
            HostName = hostName,
            UserName = userName,
            OsDisplayName = osDisplayName,
            OsBuildString = osBuildString,
            CpuName = cpuName,
            PhysicalCores = physicalCores,
            LogicalCores = logicalCores,
            BaseClockMhz = baseMhz,
            RamTotalBytes = ramTotalBytes,
            RamType = ramType,
            RamSpeedMhz = ramSpeedMhz,
            GpuName = gpuName,
            GpuVramBytes = gpuVramBytes,
            PrimaryDiskSummary = primaryDiskSummary,
            BoardName = boardName,
            BiosVersionAndDate = biosVersionAndDate,
            DisplaySummary = displaySummary
        };
    }

    private static (string OsDisplayName, string OsBuildString) ReadOsRegistryInfo()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                string productName = (key.GetValue("ProductName") as string)?.Trim() ?? "Windows";
                string displayVersion = (key.GetValue("DisplayVersion") as string)?.Trim() ?? string.Empty;
                string buildNumberStr = (key.GetValue("CurrentBuildNumber") as string)?.Trim()
                                      ?? (key.GetValue("CurrentBuild") as string)?.Trim()
                                      ?? string.Empty;
                int ubr = key.GetValue("UBR") is int u ? u : 0;

                if (int.TryParse(buildNumberStr, out int buildNum) && buildNum >= 22000 &&
                    productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
                {
                    productName = "Windows 11" + productName["Windows 10".Length..];
                }

                string fullOs = string.IsNullOrEmpty(displayVersion)
                    ? productName
                    : $"{productName} {displayVersion}";

                string buildStr = !string.IsNullOrEmpty(buildNumberStr)
                    ? (ubr > 0 ? $"{buildNumberStr}.{ubr}" : buildNumberStr)
                    : Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);

                return (fullOs, buildStr);
            }
        }
        catch { }

        return ("Windows", Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture));
    }

    private static (string CpuName, int BaseMhz) ReadCpuRegistryInfo()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key != null)
            {
                string name = (key.GetValue("ProcessorNameString") as string)?.Trim() ?? string.Empty;
                int mhz = key.GetValue("~MHz") is int m ? m : 0;
                return (name, mhz);
            }
        }
        catch { }
        return (string.Empty, 0);
    }

    private static unsafe (int PhysicalCores, int LogicalCores) QueryCoreCounts()
    {
        int logical = Math.Max(1, Environment.ProcessorCount);
        try
        {
            uint returnLength = 0;
            CsWin32PInvoke.GetLogicalProcessorInformation(null, ref returnLength);
            if (returnLength > 0)
            {
                int structSize = sizeof(SYSTEM_LOGICAL_PROCESSOR_INFORMATION);
                int count = (int)(returnLength / structSize);
                var buffer = new SYSTEM_LOGICAL_PROCESSOR_INFORMATION[count];
                fixed (SYSTEM_LOGICAL_PROCESSOR_INFORMATION* ptr = buffer)
                {
                    if (CsWin32PInvoke.GetLogicalProcessorInformation(ptr, ref returnLength))
                    {
                        int cores = 0;
                        for (int i = 0; i < count; i++)
                        {
                            if (buffer[i].Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                            {
                                cores++;
                            }
                        }
                        if (cores > 0)
                            return (cores, logical);
                    }
                }
            }
        }
        catch { }
        return (Math.Max(1, logical / 2), logical);
    }

    private static unsafe ulong QueryTotalPhysicalMemory()
    {
        try
        {
            MEMORYSTATUSEX status = new() { dwLength = (uint)sizeof(MEMORYSTATUSEX) };
            if (CsWin32PInvoke.GlobalMemoryStatusEx(ref status))
            {
                return status.ullTotalPhys;
            }
        }
        catch { }
        return 0;
    }

    private static (string RamType, int RamSpeedMhz) QueryRamWmiOnce()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT SMBIOSMemoryType, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
            using var collection = searcher.Get();
            int speed = 0;
            uint smbiosType = 0;
            foreach (ManagementBaseObject obj in collection)
            {
                using (obj)
                {
                    if (smbiosType == 0 && obj["SMBIOSMemoryType"] is uint t && t != 0)
                        smbiosType = t;
                    if (speed == 0 && obj["ConfiguredClockSpeed"] is uint cs && cs > 0)
                        speed = (int)cs;
                    else if (speed == 0 && obj["Speed"] is uint s && s > 0)
                        speed = (int)s;
                }
            }

            string typeStr = smbiosType switch
            {
                20 => "DDR",
                21 => "DDR2",
                24 => "DDR3",
                26 => "DDR4",
                30 => "LPDDR4",
                34 => "DDR5",
                35 => "LPDDR5",
                _ => string.Empty
            };

            return (typeStr, speed);
        }
        catch
        {
            return (string.Empty, 0);
        }
    }

    private static unsafe (string GpuName, ulong VramBytes) QueryPrimaryGpuDxgi()
    {
        try
        {
            HRESULT hr = CsWin32PInvoke.CreateDXGIFactory1(typeof(IDXGIFactory1).GUID, out object factoryObj);
            if (hr.Succeeded && factoryObj is IDXGIFactory1 factory)
            {
                string bestName = string.Empty;
                ulong bestVram = 0;

                for (uint i = 0; i < 16; i++)
                {
                    try
                    {
                        factory.EnumAdapters1(i, out IDXGIAdapter1 adapter);
                        if (adapter == null) break;

                        DXGI_ADAPTER_DESC1 desc = adapter.GetDesc1();
                        bool isSoftware = ((uint)desc.Flags & (uint)DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE) != 0;
                        string name = desc.Description.ToString().Trim();
                        ulong vram = desc.DedicatedVideoMemory;

                        if (!isSoftware && !string.IsNullOrWhiteSpace(name))
                        {
                            if (vram > bestVram || string.IsNullOrEmpty(bestName))
                            {
                                bestName = name;
                                bestVram = vram;
                            }
                        }
                    }
                    catch
                    {
                        break;
                    }
                }

                return (bestName, bestVram);
            }
        }
        catch { }

        return (string.Empty, 0);
    }

    private static string QueryPrimaryDiskSummary()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .ToList();
            var primary = drives.FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase))
                       ?? drives.FirstOrDefault();
            if (primary != null && primary.TotalSize > 0)
            {
                string letter = primary.Name.TrimEnd('\\');
                ulong totalBytes = (ulong)primary.TotalSize;
                ulong freeBytes = (ulong)Math.Max(0, primary.TotalFreeSpace);
                ulong usedBytes = totalBytes >= freeBytes ? totalBytes - freeBytes : 0;
                int pct = (int)Math.Round(DashboardFormatters.ClampPercent((double)usedBytes / totalBytes * 100.0));
                ulong totalGb = (ulong)Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0));
                return string.Format(CultureInfo.InvariantCulture, "{0} {1} GB  {2}% used", letter, totalGb, pct);
            }
        }
        catch { }
        return string.Empty;
    }

    private static (string BoardName, string BiosVersionAndDate) ReadBiosRegistryInfo()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (key != null)
            {
                string boardMfg = (key.GetValue("BaseBoardManufacturer") as string)?.Trim() ?? string.Empty;
                string boardProd = (key.GetValue("BaseBoardProduct") as string)?.Trim() ?? string.Empty;
                string sysProd = (key.GetValue("SystemProductName") as string)?.Trim() ?? string.Empty;

                string board = !string.IsNullOrWhiteSpace(boardProd)
                    ? (!string.IsNullOrWhiteSpace(boardMfg) && !boardProd.StartsWith(boardMfg, StringComparison.OrdinalIgnoreCase)
                        ? $"{boardMfg} {boardProd}"
                        : boardProd)
                    : sysProd;

                string biosVer = string.Empty;
                var rawBiosVer = key.GetValue("BIOSVersion");
                if (rawBiosVer is string sVer)
                    biosVer = sVer.Trim();
                else if (rawBiosVer is string[] arrVer && arrVer.Length > 0)
                    biosVer = arrVer[0].Trim();

                string biosDate = (key.GetValue("BIOSReleaseDate") as string)?.Trim() ?? string.Empty;
                if (DateTime.TryParseExact(biosDate, ["MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    biosDate = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }

                string biosCombined = $"{biosVer} {biosDate}".Trim();
                return (board, biosCombined);
            }
        }
        catch { }
        return (string.Empty, string.Empty);
    }

    private static unsafe string QueryPrimaryDisplaySettings()
    {
        try
        {
            DEVMODEW devMode = new() { dmSize = (ushort)sizeof(DEVMODEW) };
            if (CsWin32PInvoke.EnumDisplaySettings(null, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref devMode))
            {
                uint w = devMode.dmPelsWidth;
                uint h = devMode.dmPelsHeight;
                uint hz = devMode.dmDisplayFrequency;
                if (w > 0 && h > 0)
                {
                    return hz > 1
                        ? string.Format(CultureInfo.InvariantCulture, "{0}x{1} @{2} Hz", w, h, hz)
                        : string.Format(CultureInfo.InvariantCulture, "{0}x{1}", w, h);
                }
            }
        }
        catch { }
        return string.Empty;
    }

    private static T TryGet<T>(Func<T> getter, T fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }
}

public sealed class CpuSource : ICpuSource
{
    private readonly int _logicalCount;
    private long[] _prevIdle;
    private long[] _prevKernel;
    private long[] _prevUser;
    private bool _hasBaseline;

    private IntPtr _pdhQuery = IntPtr.Zero;
    private IntPtr _pdhTempCounter = IntPtr.Zero;
    private bool _pdhTempInitialized;
    private bool _pdhTempSupported;

    public CpuSource()
    {
        _logicalCount = Math.Max(1, Environment.ProcessorCount);
        _prevIdle = new long[_logicalCount];
        _prevKernel = new long[_logicalCount];
        _prevUser = new long[_logicalCount];

        // Prime initial baseline so the first tick has valid deltas
        SampleProcessorTimes(out _);
        InitializeThermalCounter();
    }

    private unsafe void InitializeThermalCounter()
    {
        if (_pdhTempInitialized) return;
        _pdhTempInitialized = true;
        try
        {
            fixed (IntPtr* pQuery = &_pdhQuery)
            {
                uint status = CsWin32PInvoke.PdhOpenQuery(null, 0, pQuery);
                if (status != 0 || _pdhQuery == IntPtr.Zero) return;
            }

            fixed (char* pathPtr = @"\Thermal Zone Information(*)\Temperature")
            fixed (IntPtr* pCounter = &_pdhTempCounter)
            {
                uint status = CsWin32PInvoke.PdhAddEnglishCounter(_pdhQuery, pathPtr, 0, pCounter);
                if (status == 0 && _pdhTempCounter != IntPtr.Zero)
                {
                    uint collectStatus = CsWin32PInvoke.PdhCollectQueryData(_pdhQuery);
                    _pdhTempSupported = collectStatus == 0;
                }
            }
        }
        catch
        {
            _pdhTempSupported = false;
        }
    }

    public CpuSample Sample()
    {
        double[] perCore = SampleProcessorTimes(out double totalPercent);
        double? tempC = SampleThermalCelsius();

        return new CpuSample
        {
            TotalPercent = totalPercent,
            PerCorePercent = perCore,
            TemperatureCelsius = tempC
        };
    }

    private unsafe double[] SampleProcessorTimes(out double totalPercent)
    {
        var perCore = new double[_logicalCount];
        totalPercent = 0.0;

        try
        {
            int structSize = sizeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION);
            uint bufferBytes = (uint)(structSize * _logicalCount);
            var infos = new SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[_logicalCount];
            uint returnLength = 0;

            fixed (SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION* ptr = infos)
            {
                NTSTATUS status = Windows.Wdk.CsWin32PInvoke.NtQuerySystemInformation(
                    SYSTEM_INFORMATION_CLASS.SystemProcessorPerformanceInformation,
                    ptr,
                    bufferBytes,
                    &returnLength);

                if (status.SeverityCode == NTSTATUS.Severity.Success)
                {
                    int returnedCount = Math.Min(_logicalCount, (int)(returnLength / structSize));
                    long sumBusy = 0;
                    long sumTotal = 0;

                    for (int i = 0; i < returnedCount; i++)
                    {
                        long curIdle = infos[i].IdleTime;
                        long curKernel = infos[i].KernelTime;
                        long curUser = infos[i].UserTime;

                        if (_hasBaseline)
                        {
                            long dIdle = curIdle - _prevIdle[i];
                            long dKernel = curKernel - _prevKernel[i];
                            long dUser = curUser - _prevUser[i];

                            // In SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION, KernelTime includes IdleTime
                            long dTotal = dKernel + dUser;
                            long dBusy = dTotal - dIdle;

                            if (dTotal > 0 && dBusy >= 0)
                            {
                                perCore[i] = DashboardFormatters.ClampPercent((double)dBusy / dTotal * 100.0);
                                sumBusy += dBusy;
                                sumTotal += dTotal;
                            }
                        }

                        _prevIdle[i] = curIdle;
                        _prevKernel[i] = curKernel;
                        _prevUser[i] = curUser;
                    }

                    _hasBaseline = true;
                    if (sumTotal > 0 && sumBusy >= 0)
                    {
                        totalPercent = DashboardFormatters.ClampPercent((double)sumBusy / sumTotal * 100.0);
                    }
                }
            }
        }
        catch { }

        return perCore;
    }

    private unsafe double? SampleThermalCelsius()
    {
        if (!_pdhTempSupported || _pdhQuery == IntPtr.Zero || _pdhTempCounter == IntPtr.Zero)
            return null;

        try
        {
            if (CsWin32PInvoke.PdhCollectQueryData(_pdhQuery) != 0)
                return null;

            uint counterType = 0;
            PDH_FMT_COUNTERVALUE val = default;
            // PDH_FMT_DOUBLE = 0x00000200
            uint status = CsWin32PInvoke.PdhGetFormattedCounterValue(_pdhTempCounter, (PDH_FMT)0x00000200u, &counterType, out val);
            if (status == 0)
            {
                double kelvin = val.Anonymous.doubleValue;
                double celsius = kelvin > 200.0 ? kelvin - 273.15 : kelvin;
                if (!double.IsNaN(celsius) && !double.IsInfinity(celsius) && celsius >= 5.0 && celsius <= 125.0)
                {
                    return Math.Round(celsius, 1);
                }
            }
        }
        catch { }

        return null;
    }

    public void Dispose()
    {
        if (_pdhQuery != IntPtr.Zero)
        {
            try { CsWin32PInvoke.PdhCloseQuery(_pdhQuery); } catch { }
            _pdhQuery = IntPtr.Zero;
            _pdhTempCounter = IntPtr.Zero;
        }
    }
}

public sealed class MemorySource : IMemorySource
{
    public unsafe MemorySample Sample()
    {
        ulong totalBytes = 0;
        ulong usedBytes = 0;
        double usedPercent = 0.0;
        ulong? cachedBytes = null;
        ulong? commitTotalBytes = null;
        ulong? commitLimitBytes = null;
        ulong? pagedPoolBytes = null;

        try
        {
            MEMORYSTATUSEX memStatus = new() { dwLength = (uint)sizeof(MEMORYSTATUSEX) };
            if (CsWin32PInvoke.GlobalMemoryStatusEx(ref memStatus) && memStatus.ullTotalPhys > 0)
            {
                totalBytes = memStatus.ullTotalPhys;
                usedBytes = memStatus.ullTotalPhys >= memStatus.ullAvailPhys
                    ? memStatus.ullTotalPhys - memStatus.ullAvailPhys
                    : 0;
                usedPercent = DashboardFormatters.ClampPercent((double)usedBytes / totalBytes * 100.0);
            }
        }
        catch { }

        try
        {
            PERFORMANCE_INFORMATION perfInfo = new() { cb = (uint)sizeof(PERFORMANCE_INFORMATION) };
            if (CsWin32PInvoke.K32GetPerformanceInfo(ref perfInfo, perfInfo.cb))
            {
                ulong pageSize = perfInfo.PageSize;
                if (pageSize > 0)
                {
                    cachedBytes = perfInfo.SystemCache * pageSize;
                    commitTotalBytes = perfInfo.CommitTotal * pageSize;
                    commitLimitBytes = perfInfo.CommitLimit * pageSize;
                    pagedPoolBytes = perfInfo.KernelPaged * pageSize;
                }
            }
        }
        catch { }

        return new MemorySample
        {
            TotalBytes = totalBytes,
            UsedBytes = usedBytes,
            UsedPercent = usedPercent,
            CachedBytes = cachedBytes,
            CommitTotalBytes = commitTotalBytes,
            CommitLimitBytes = commitLimitBytes,
            PagedPoolBytes = pagedPoolBytes
        };
    }
}

public sealed class StorageSource : IStorageSource
{
    private IntPtr _pdhQuery = IntPtr.Zero;
    private IntPtr _pdhReadCounter = IntPtr.Zero;
    private IntPtr _pdhWriteCounter = IntPtr.Zero;
    private bool _pdhSupported;

    public StorageSource()
    {
        InitializePdhDiskCounters();
    }

    private unsafe void InitializePdhDiskCounters()
    {
        try
        {
            fixed (IntPtr* pQuery = &_pdhQuery)
            {
                if (CsWin32PInvoke.PdhOpenQuery(null, 0, pQuery) != 0 || _pdhQuery == IntPtr.Zero)
                    return;
            }

            fixed (char* readPath = @"\PhysicalDisk(_Total)\Disk Read Bytes/sec")
            fixed (IntPtr* pRead = &_pdhReadCounter)
            {
                CsWin32PInvoke.PdhAddEnglishCounter(_pdhQuery, readPath, 0, pRead);
            }

            fixed (char* writePath = @"\PhysicalDisk(_Total)\Disk Write Bytes/sec")
            fixed (IntPtr* pWrite = &_pdhWriteCounter)
            {
                CsWin32PInvoke.PdhAddEnglishCounter(_pdhQuery, writePath, 0, pWrite);
            }

            if (_pdhReadCounter != IntPtr.Zero && _pdhWriteCounter != IntPtr.Zero)
            {
                _pdhSupported = CsWin32PInvoke.PdhCollectQueryData(_pdhQuery) == 0;
            }
        }
        catch
        {
            _pdhSupported = false;
        }
    }

    public unsafe StorageSample Sample()
    {
        var drives = new List<DriveCapacitySample>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || d.DriveType != DriveType.Fixed || d.TotalSize <= 0)
                        continue;

                    ulong total = (ulong)d.TotalSize;
                    ulong free = (ulong)Math.Max(0, d.TotalFreeSpace);
                    ulong used = total >= free ? total - free : 0;
                    double pct = DashboardFormatters.ClampPercent((double)used / total * 100.0);

                    drives.Add(new DriveCapacitySample
                    {
                        Name = d.Name.TrimEnd('\\'),
                        UsedBytes = used,
                        TotalBytes = total,
                        UsedPercent = pct
                    });
                }
                catch { }
            }
        }
        catch { }

        double? readBps = null;
        double? writeBps = null;

        if (_pdhSupported && _pdhQuery != IntPtr.Zero)
        {
            try
            {
                if (CsWin32PInvoke.PdhCollectQueryData(_pdhQuery) == 0)
                {
                    uint counterType = 0;
                    if (CsWin32PInvoke.PdhGetFormattedCounterValue(_pdhReadCounter, (PDH_FMT)0x00000200u, &counterType, out var rVal) == 0)
                    {
                        readBps = DashboardFormatters.SanitizeNonNegative(rVal.Anonymous.doubleValue);
                    }
                    if (CsWin32PInvoke.PdhGetFormattedCounterValue(_pdhWriteCounter, (PDH_FMT)0x00000200u, &counterType, out var wVal) == 0)
                    {
                        writeBps = DashboardFormatters.SanitizeNonNegative(wVal.Anonymous.doubleValue);
                    }
                }
            }
            catch { }
        }

        return new StorageSample
        {
            Drives = drives,
            ReadBytesPerSec = readBps,
            WriteBytesPerSec = writeBps
        };
    }

    public void Dispose()
    {
        if (_pdhQuery != IntPtr.Zero)
        {
            try { CsWin32PInvoke.PdhCloseQuery(_pdhQuery); } catch { }
            _pdhQuery = IntPtr.Zero;
        }
    }
}

public sealed class NetworkSource : INetworkSource
{
    private string? _lastInterfaceId;
    private long _prevRxBytes;
    private long _prevTxBytes;
    private long _prevTimestampTicks;

    public NetworkSample Sample()
    {
        long nowTicks = Stopwatch.GetTimestamp();
        double elapsedSec = _prevTimestampTicks > 0
            ? (nowTicks - _prevTimestampTicks) / (double)Stopwatch.Frequency
            : 0.0;
        _prevTimestampTicks = nowTicks;

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .ToList();

            // Prefer adapter with an IPv4 default gateway, then highest link speed
            var active = interfaces
                .OrderByDescending(ni =>
                {
                    try
                    {
                        return ni.GetIPProperties().GatewayAddresses
                            .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(g.Address));
                    }
                    catch { return false; }
                })
                .ThenByDescending(ni => ni.Speed)
                .FirstOrDefault();

            if (active == null)
            {
                _lastInterfaceId = null;
                return EmptyNetworkSample();
            }

            var ipProps = active.GetIPProperties();
            string ipv4 = ipProps.UnicastAddresses
                .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? string.Empty;
            string gateway = ipProps.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(g.Address))?.Address.ToString() ?? string.Empty;

            byte[] macBytes = active.GetPhysicalAddress().GetAddressBytes();
            string mac = macBytes.Length > 0
                ? string.Join(":", macBytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)))
                : string.Empty;

            var stats = active.GetIPStatistics();
            long curRx = stats.BytesReceived;
            long curTx = stats.BytesSent;

            double rxRate = 0.0;
            double txRate = 0.0;
            if (string.Equals(_lastInterfaceId, active.Id, StringComparison.Ordinal) && elapsedSec > 0.05)
            {
                rxRate = DashboardFormatters.ComputeSignedCounterRate(_prevRxBytes, curRx, elapsedSec);
                txRate = DashboardFormatters.ComputeSignedCounterRate(_prevTxBytes, curTx, elapsedSec);
            }

            _lastInterfaceId = active.Id;
            _prevRxBytes = curRx;
            _prevTxBytes = curTx;

            long linkMbps = active.Speed > 0 ? active.Speed / 1_000_000 : 0;
            bool isWifi = active.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            string kindLabel = isWifi ? "Wi-Fi" : "Ethernet";
            int? rssiDbm = isWifi ? TryQueryWifiRssiDbm() : null;

            return new NetworkSample
            {
                IsAvailable = true,
                AdapterName = active.Description?.Trim() ?? active.Name,
                AdapterKindLabel = kindLabel,
                Ipv4Address = ipv4,
                MacAddress = mac,
                GatewayAddress = gateway,
                LinkSpeedMbps = linkMbps,
                WifiRssiDbm = rssiDbm,
                RxBytesPerSec = rxRate,
                TxBytesPerSec = txRate
            };
        }
        catch
        {
            return EmptyNetworkSample();
        }
    }

    private static unsafe int? TryQueryWifiRssiDbm()
    {
        HANDLE clientHandle = default;
        WLAN_INTERFACE_INFO_LIST* pIfList = null;
        try
        {
            uint negotiatedVersion = 0;
            uint err = CsWin32PInvoke.WlanOpenHandle(2, null, &negotiatedVersion, &clientHandle);
            if (err != 0 || clientHandle.IsNull)
                return null;

            err = CsWin32PInvoke.WlanEnumInterfaces(clientHandle, null, &pIfList);
            if (err != 0 || pIfList == null || pIfList->dwNumberOfItems == 0)
                return null;

            Guid ifGuid = pIfList->InterfaceInfo[0].InterfaceGuid;

            // 1. Try direct RSSI opcode (wlan_intf_opcode_rssi = 0x10000102)
            uint dataSize = 0;
            void* pData = null;
            err = CsWin32PInvoke.WlanQueryInterface(
                clientHandle,
                &ifGuid,
                WLAN_INTF_OPCODE.wlan_intf_opcode_rssi,
                null,
                &dataSize,
                &pData,
                null);
            if (err == 0 && pData != null && dataSize >= sizeof(int))
            {
                int rssi = *(int*)pData;
                CsWin32PInvoke.WlanFreeMemory(pData);
                if (rssi >= -110 && rssi <= -10)
                    return rssi;
            }
            else if (pData != null)
            {
                CsWin32PInvoke.WlanFreeMemory(pData);
                pData = null;
            }

            // 2. Fallback to current connection attributes (wlanSignalQuality 0..100 -> dBm)
            err = CsWin32PInvoke.WlanQueryInterface(
                clientHandle,
                &ifGuid,
                WLAN_INTF_OPCODE.wlan_intf_opcode_current_connection,
                null,
                &dataSize,
                &pData,
                null);
            if (err == 0 && pData != null && dataSize >= sizeof(WLAN_CONNECTION_ATTRIBUTES))
            {
                var conn = *(WLAN_CONNECTION_ATTRIBUTES*)pData;
                uint quality = conn.wlanAssociationAttributes.wlanSignalQuality;
                CsWin32PInvoke.WlanFreeMemory(pData);
                if (quality <= 100)
                {
                    return (int)(-100 + (quality / 2));
                }
            }
            else if (pData != null)
            {
                CsWin32PInvoke.WlanFreeMemory(pData);
            }
        }
        catch { }
        finally
        {
            if (pIfList != null)
            {
                try { CsWin32PInvoke.WlanFreeMemory(pIfList); } catch { }
            }
            if (!clientHandle.IsNull)
            {
                try { CsWin32PInvoke.WlanCloseHandle(clientHandle, null); } catch { }
            }
        }
        return null;
    }

    private static NetworkSample EmptyNetworkSample() => new()
    {
        IsAvailable = false,
        AdapterName = string.Empty,
        AdapterKindLabel = string.Empty,
        Ipv4Address = string.Empty,
        MacAddress = string.Empty,
        GatewayAddress = string.Empty,
        LinkSpeedMbps = 0,
        WifiRssiDbm = null,
        RxBytesPerSec = 0.0,
        TxBytesPerSec = 0.0
    };

    public void Dispose() { }
}

public sealed class ProcessSource : IProcessSource
{
    private readonly int _logicalProcessors = Math.Max(1, Environment.ProcessorCount);
    private Dictionary<int, TimeSpan> _prevCpuTimes = new();
    private long _prevSampleTicks;

    public ProcessSnapshot Sample()
    {
        long nowTicks = Stopwatch.GetTimestamp();
        double elapsedSec = _prevSampleTicks > 0
            ? (nowTicks - _prevSampleTicks) / (double)Stopwatch.Frequency
            : 0.0;
        _prevSampleTicks = nowTicks;

        var parentMap = CaptureParentPidMap();
        var nextCpuTimes = new Dictionary<int, TimeSpan>(256);
        var allProcesses = new Dictionary<int, ProcessIdentity>(256);
        var samples = new List<ProcessSample>(128);

        Process[] processes = [];
        try
        {
            processes = Process.GetProcesses();
        }
        catch { }

        foreach (var proc in processes)
        {
            using (proc)
            {
                try
                {
                    int pid = proc.Id;
                    if (pid <= 4) continue; // Skip Idle (0) and System (4) pseudo-processes

                    string rawName = proc.ProcessName;
                    if (string.IsNullOrWhiteSpace(rawName)) continue;
                    string exeName = rawName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? rawName
                        : rawName + ".exe";

                    parentMap.TryGetValue(pid, out int parentPid);
                    allProcesses[pid] = new ProcessIdentity(pid, exeName, parentPid);

                    ulong workingSet = 0;
                    try
                    {
                        workingSet = (ulong)Math.Max(0L, proc.WorkingSet64);
                    }
                    catch { }

                    double cpuPct = 0.0;
                    try
                    {
                        TimeSpan curTotal = proc.TotalProcessorTime;
                        nextCpuTimes[pid] = curTotal;

                        if (elapsedSec > 0.05 && _prevCpuTimes.TryGetValue(pid, out TimeSpan prevTotal))
                        {
                            double deltaSec = (curTotal - prevTotal).TotalSeconds;
                            if (deltaSec > 0.0)
                            {
                                cpuPct = DashboardFormatters.ClampPercent((deltaSec / (elapsedSec * _logicalProcessors)) * 100.0);
                            }
                        }
                    }
                    catch { }

                    samples.Add(new ProcessSample
                    {
                        Pid = pid,
                        Name = exeName,
                        CpuPercent = Math.Round(cpuPct, 1),
                        WorkingSetBytes = workingSet
                    });
                }
                catch
                {
                    // Process exited or access denied
                }
            }
        }

        _prevCpuTimes = nextCpuTimes;

        var top8 = samples
            .OrderByDescending(p => p.CpuPercent)
            .ThenByDescending(p => p.WorkingSetBytes)
            .Take(8)
            .ToList();

        return new ProcessSnapshot
        {
            TopProcesses = top8,
            AllProcesses = allProcesses
        };
    }

    private static unsafe Dictionary<int, int> CaptureParentPidMap()
    {
        var map = new Dictionary<int, int>(256);
        HANDLE snap = default;
        try
        {
            snap = CsWin32PInvoke.CreateToolhelp32Snapshot(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
            if (snap.IsNull || (nint)snap.Value == -1)
                return map;

            PROCESSENTRY32W entry = new() { dwSize = (uint)sizeof(PROCESSENTRY32W) };
            if (CsWin32PInvoke.Process32FirstW(snap, &entry))
            {
                do
                {
                    map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
                }
                while (CsWin32PInvoke.Process32NextW(snap, &entry));
            }
        }
        catch { }
        finally
        {
            if (!snap.IsNull && (nint)snap.Value != -1)
            {
                try { CsWin32PInvoke.CloseHandle(snap); } catch { }
            }
        }
        return map;
    }
}

public sealed class ConnectionSource : IConnectionSource
{
    private const uint AF_INET = 2;

    public unsafe ConnectionSnapshot Sample(IReadOnlyDictionary<int, ProcessIdentity> processMap)
    {
        int establishedCount = 0;
        int listenCount = 0;
        var establishedMap = new Dictionary<ConnectionKey, ConnectionSample>();
        var candidateRows = new List<(int Priority, ConnectionSample Row)>(64);

        // 1. IPv4 TCP Table with Owning PID
        try
        {
            uint size = 0;
            CsWin32PInvoke.GetExtendedTcpTable(null, ref size, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
            if (size > 0)
            {
                byte[] buffer = new byte[size];
                fixed (byte* pBuf = buffer)
                {
                    if (CsWin32PInvoke.GetExtendedTcpTable(pBuf, ref size, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0) == 0)
                    {
                        uint numEntries = *(uint*)pBuf;
                        var rowsPtr = (MIB_TCPROW_OWNER_PID*)(pBuf + sizeof(uint));
                        int maxEntries = (int)Math.Min(numEntries, (size - sizeof(uint)) / (uint)sizeof(MIB_TCPROW_OWNER_PID));

                        for (int i = 0; i < maxEntries; i++)
                        {
                            var row = rowsPtr[i];
                            uint stateCode = (uint)row.dwState;
                            int pid = (int)row.dwOwningPid;
                            string localIp = FormatIpv4(row.dwLocalAddr);
                            int localPort = NetworkToHostPort(row.dwLocalPort);
                            string remoteIp = FormatIpv4(row.dwRemoteAddr);
                            int remotePort = NetworkToHostPort(row.dwRemotePort);

                            string procName = ResolveProcessName(pid, processMap);

                            // MIB_TCP_STATE: 2=LISTEN, 5=ESTAB, 8=CLOSE_WAIT, 11=TIME_WAIT
                            if (stateCode == 5)
                            {
                                establishedCount++;
                                string localEp = $"{localIp}:{localPort}";
                                string remoteEp = $"{remoteIp}:{remotePort}";
                                var key = new ConnectionKey("TCP", localEp, remoteEp, pid);
                                var sample = new ConnectionSample
                                {
                                    Protocol = "TCP",
                                    RemoteEndpoint = remoteEp,
                                    State = "ESTAB",
                                    ProcessName = procName,
                                    OwningPid = pid
                                };
                                establishedMap[key] = sample;
                                int prio = remoteIp.StartsWith("127.", StringComparison.Ordinal) ? 2 : 1;
                                candidateRows.Add((prio, sample));
                            }
                            else if (stateCode == 2)
                            {
                                listenCount++;
                                string listenEp = $"{localIp}:{localPort}";
                                candidateRows.Add((5, new ConnectionSample
                                {
                                    Protocol = "TCP",
                                    RemoteEndpoint = listenEp,
                                    State = "LISTEN",
                                    ProcessName = procName,
                                    OwningPid = pid
                                }));
                            }
                            else if (stateCode == 11 || stateCode == 8)
                            {
                                string remoteEp = $"{remoteIp}:{remotePort}";
                                candidateRows.Add((3, new ConnectionSample
                                {
                                    Protocol = "TCP",
                                    RemoteEndpoint = remoteEp,
                                    State = stateCode == 11 ? "TIME_WAIT" : "CLOSE_WAIT",
                                    ProcessName = procName,
                                    OwningPid = pid
                                }));
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // 2. IPv4 UDP Table with Owning PID
        try
        {
            uint size = 0;
            CsWin32PInvoke.GetExtendedUdpTable(null, ref size, false, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
            if (size > 0)
            {
                byte[] buffer = new byte[size];
                fixed (byte* pBuf = buffer)
                {
                    if (CsWin32PInvoke.GetExtendedUdpTable(pBuf, ref size, false, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0) == 0)
                    {
                        uint numEntries = *(uint*)pBuf;
                        var rowsPtr = (MIB_UDPROW_OWNER_PID*)(pBuf + sizeof(uint));
                        int maxEntries = (int)Math.Min(numEntries, (size - sizeof(uint)) / (uint)sizeof(MIB_UDPROW_OWNER_PID));

                        for (int i = 0; i < maxEntries; i++)
                        {
                            var row = rowsPtr[i];
                            int pid = (int)row.dwOwningPid;
                            if (pid <= 4) continue;
                            string localIp = FormatIpv4(row.dwLocalAddr);
                            int localPort = NetworkToHostPort(row.dwLocalPort);
                            if (localPort <= 0) continue;

                            string procName = ResolveProcessName(pid, processMap);
                            candidateRows.Add((4, new ConnectionSample
                            {
                                Protocol = "UDP",
                                RemoteEndpoint = $"{localIp}:{localPort}",
                                State = "--",
                                ProcessName = procName,
                                OwningPid = pid
                            }));
                        }
                    }
                }
            }
        }
        catch { }

        var activeRows = candidateRows
            .OrderBy(c => c.Priority)
            .Select(c => c.Row)
            .DistinctBy(r => (r.Protocol, r.RemoteEndpoint, r.State, r.ProcessName))
            .Take(8)
            .ToList();

        return new ConnectionSnapshot
        {
            EstablishedCount = establishedCount,
            ListenCount = listenCount,
            ActiveRows = activeRows,
            EstablishedConnections = establishedMap
        };
    }

    private static string ResolveProcessName(int pid, IReadOnlyDictionary<int, ProcessIdentity> processMap)
    {
        if (pid == 0) return "Idle";
        if (pid == 4) return "System";
        if (processMap.TryGetValue(pid, out var identity) && !string.IsNullOrWhiteSpace(identity.Name))
            return identity.Name;
        return $"pid:{pid}";
    }

    private static string FormatIpv4(uint dwAddr)
    {
        uint b0 = dwAddr & 0xFF;
        uint b1 = (dwAddr >> 8) & 0xFF;
        uint b2 = (dwAddr >> 16) & 0xFF;
        uint b3 = (dwAddr >> 24) & 0xFF;
        return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}", b0, b1, b2, b3);
    }

    private static int NetworkToHostPort(uint dwPort)
    {
        return (int)(((dwPort & 0xFF) << 8) | ((dwPort >> 8) & 0xFF));
    }
}

public static class PowerAndUptimeHelper
{
    public static TimeSpan GetSystemUptime()
    {
        long ms = Environment.TickCount64;
        return TimeSpan.FromMilliseconds(Math.Max(0L, ms));
    }

    public static string? QueryBatteryStatusText()
    {
        try
        {
            if (CsWin32PInvoke.GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
            {
                // BatteryFlag 128 = No system battery
                if (status.BatteryFlag == 128 || status.BatteryLifePercent > 100)
                {
                    return status.ACLineStatus == 1 ? "AC power" : null;
                }

                bool onAc = status.ACLineStatus == 1;
                bool charging = (status.BatteryFlag & 8) != 0;
                string stateTag = charging ? "charging" : (onAc ? "AC power" : "discharging");
                return string.Format(CultureInfo.InvariantCulture, "{0}% ({1})", status.BatteryLifePercent, stateTag);
            }
        }
        catch { }
        return null;
    }
}
