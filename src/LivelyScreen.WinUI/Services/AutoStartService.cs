using Microsoft.Win32;
using System.Diagnostics;

namespace LivelyScreen.Services;

public static class AutoStartService
{
    private const string AppName = "LivelyScreen";
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\" --autostart");
                }
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set autostart: {ex.Message}");
        }
    }

    public static void SyncAutoStartRegistration()
    {
        try
        {
            if (IsAutoStartEnabled())
            {
                SetAutoStart(true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to sync autostart registration: {ex.Message}");
        }
    }

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }
}
