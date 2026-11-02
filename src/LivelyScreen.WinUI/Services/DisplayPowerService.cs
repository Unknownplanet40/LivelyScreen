using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Microsoft.UI.Windowing;
using LivelyScreen_WinUI;

namespace LivelyScreen.Services;

public class DisplayPowerService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetVCPFeature(IntPtr hPhysicalMonitor, byte bVCPCode, uint dwNewValue);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(
        IntPtr hPhysicalMonitor,
        byte bVCPCode,
        out uint pvct,
        out uint pdwCurrentValue,
        out uint pdwMaximumValue);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const byte VCP_POWER_MODE = 0xD6;
    private const uint VCP_POWER_ON = 0x01;
    private const uint VCP_POWER_STANDBY = 0x04;      // D3 / Standby
    private const uint VCP_POWER_STANDBY_ALT = 0x02;  // D1 / Standby
    private const uint MOUSEEVENTF_MOVE = 0x0001;

    private readonly object _lock = new();
    private readonly List<PHYSICAL_MONITOR[]> _managedPhysicalMonitors = new();

    public bool TryPowerOffDisplay(DisplayArea display)
    {
        lock (_lock)
        {
            try
            {
                var bounds = display.OuterBounds;
                var rect = new RECT
                {
                    Left = bounds.X,
                    Top = bounds.Y,
                    Right = bounds.X + bounds.Width,
                    Bottom = bounds.Y + bounds.Height
                };

                IntPtr hMonitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST);
                if (hMonitor == IntPtr.Zero)
                {
                    App.Log($"DisplayPowerService: MonitorFromRect failed for bounds ({bounds.X},{bounds.Y},{bounds.Width},{bounds.Height})");
                    return false;
                }

                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint monitorCount) || monitorCount == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    App.Log($"DisplayPowerService: GetNumberOfPhysicalMonitors failed (count={monitorCount}, err={err})");
                    return false;
                }

                var physicalMonitors = new PHYSICAL_MONITOR[monitorCount];
                if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, monitorCount, physicalMonitors))
                {
                    int err = Marshal.GetLastWin32Error();
                    App.Log($"DisplayPowerService: GetPhysicalMonitors failed (err={err})");
                    return false;
                }

                bool anySuccess = false;
                foreach (var pm in physicalMonitors)
                {
                    App.Log($"DisplayPowerService: Sending DDC/CI standby to physical monitor '{pm.szPhysicalMonitorDescription}'");
                    bool ok = SetVCPFeature(pm.hPhysicalMonitor, VCP_POWER_MODE, VCP_POWER_STANDBY);
                    if (!ok)
                    {
                        ok = SetVCPFeature(pm.hPhysicalMonitor, VCP_POWER_MODE, VCP_POWER_STANDBY_ALT);
                    }

                    if (ok)
                    {
                        anySuccess = true;
                    }
                    else
                    {
                        int err = Marshal.GetLastWin32Error();
                        App.Log($"DisplayPowerService: SetVCPFeature failed for '{pm.szPhysicalMonitorDescription}' (err={err})");
                    }
                }

                if (anySuccess)
                {
                    _managedPhysicalMonitors.Add(physicalMonitors);
                    App.Log($"DisplayPowerService: Successfully placed display into DDC/CI standby.");
                    return true;
                }
                else
                {
                    DestroyPhysicalMonitors(monitorCount, physicalMonitors);
                    return false;
                }
            }
            catch (Exception ex)
            {
                App.Log($"DisplayPowerService: Exception during TryPowerOffDisplay: {ex.Message}");
                return false;
            }
        }
    }

    public bool CheckDisplayDdcCiSupport(DisplayArea display)
    {
        try
        {
            var bounds = display.OuterBounds;
            var rect = new RECT
            {
                Left = bounds.X,
                Top = bounds.Y,
                Right = bounds.X + bounds.Width,
                Bottom = bounds.Y + bounds.Height
            };

            IntPtr hMonitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero)
                return false;

            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
                return false;

            var pms = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, pms))
                return false;

            bool supported = false;
            try
            {
                foreach (var pm in pms)
                {
                    if (GetVCPFeatureAndVCPFeatureReply(pm.hPhysicalMonitor, VCP_POWER_MODE, out _, out _, out _))
                    {
                        supported = true;
                        break;
                    }
                }
            }
            finally
            {
                DestroyPhysicalMonitors(count, pms);
            }

            return supported;
        }
        catch (Exception ex)
        {
            App.Log($"DisplayPowerService: CheckDisplayDdcCiSupport exception: {ex.Message}");
            return false;
        }
    }

    public void WakeAllDisplays()
    {
        lock (_lock)
        {
            if (_managedPhysicalMonitors.Count == 0)
                return;

            App.Log($"DisplayPowerService: Waking {_managedPhysicalMonitors.Count} monitor group(s)...");

            foreach (var monitors in _managedPhysicalMonitors)
            {
                try
                {
                    foreach (var pm in monitors)
                    {
                        App.Log($"DisplayPowerService: Sending DDC/CI Power On to '{pm.szPhysicalMonitorDescription}'");
                        SetVCPFeature(pm.hPhysicalMonitor, VCP_POWER_MODE, VCP_POWER_ON);
                    }
                    DestroyPhysicalMonitors((uint)monitors.Length, monitors);
                }
                catch (Exception ex)
                {
                    App.Log($"DisplayPowerService: Error waking physical monitors: {ex.Message}");
                }
            }

            _managedPhysicalMonitors.Clear();

            // Input ping to cause Windows DWM and GPU graphics stack to re-handshake waking displays
            try
            {
                mouse_event(MOUSEEVENTF_MOVE, 1, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_MOVE, -1, 0, 0, UIntPtr.Zero);
                App.Log("DisplayPowerService: Sent wake mouse ping");
            }
            catch (Exception ex)
            {
                App.Log($"DisplayPowerService: Error sending wake input ping: {ex.Message}");
            }
        }
    }
}
