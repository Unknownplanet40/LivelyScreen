using System.Globalization;

namespace LivelyScreen.Services.Dashboard;

public static class DashboardFormatters
{
    public static double ClampPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            return 0.0;
        if (value > 100.0)
            return 100.0;
        return value;
    }

    public static double SanitizeNonNegative(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            return 0.0;
        return value;
    }

    /// <summary>
    /// Computes a non-negative rate per second from monotonic cumulative counters.
    /// Returns 0 on counter reset (current &lt; previous) or invalid elapsed time.
    /// </summary>
    public static double ComputeCounterRate(ulong previousCounter, ulong currentCounter, double elapsedSeconds)
    {
        if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds <= 0.0001)
            return 0.0;
        if (currentCounter <= previousCounter)
            return 0.0;

        ulong delta = currentCounter - previousCounter;
        double rate = delta / elapsedSeconds;
        return SanitizeNonNegative(rate);
    }

    public static double ComputeSignedCounterRate(long previousCounter, long currentCounter, double elapsedSeconds)
    {
        if (previousCounter < 0 || currentCounter < 0 || currentCounter <= previousCounter)
            return 0.0;
        return ComputeCounterRate((ulong)previousCounter, (ulong)currentCounter, elapsedSeconds);
    }

    public static string FormatGigabytes(ulong bytes, int decimals = 1)
    {
        double gb = bytes / (1024.0 * 1024.0 * 1024.0);
        return decimals <= 0
            ? string.Format(CultureInfo.InvariantCulture, "{0:F0} GB", Math.Round(gb))
            : string.Format(CultureInfo.InvariantCulture, $"{{0:F{decimals}}} GB", gb);
    }

    public static string FormatMegabytes(ulong bytes)
    {
        ulong mb = (ulong)Math.Round(bytes / (1024.0 * 1024.0));
        return string.Format(CultureInfo.InvariantCulture, "{0} MB", mb);
    }

    public static string FormatDriveUsage(ulong usedBytes, ulong totalBytes)
    {
        ulong usedGb = (ulong)Math.Round(usedBytes / (1024.0 * 1024.0 * 1024.0));
        ulong totalGb = (ulong)Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0));
        return string.Format(CultureInfo.InvariantCulture, "{0}/{1} GB", usedGb, totalGb);
    }

    public static string FormatRateBytesPerSec(double bytesPerSec)
    {
        bytesPerSec = SanitizeNonNegative(bytesPerSec);
        const double kb = 1024.0;
        const double mb = kb * 1024.0;
        const double gb = mb * 1024.0;

        if (bytesPerSec >= gb)
            return string.Format(CultureInfo.InvariantCulture, "{0:F2} GB/s", bytesPerSec / gb);
        if (bytesPerSec >= mb * 10.0)
            return string.Format(CultureInfo.InvariantCulture, "{0:F1} MB/s", bytesPerSec / mb);
        if (bytesPerSec >= mb)
            return string.Format(CultureInfo.InvariantCulture, "{0:F2} MB/s", bytesPerSec / mb);
        if (bytesPerSec >= kb)
            return string.Format(CultureInfo.InvariantCulture, "{0:F0} KB/s", bytesPerSec / kb);
        return string.Format(CultureInfo.InvariantCulture, "{0:F0} B/s", bytesPerSec);
    }

    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero)
            uptime = TimeSpan.Zero;

        int days = (int)uptime.TotalDays;
        return string.Format(
            CultureInfo.InvariantCulture,
            "uptime {0}d {1:D2}:{2:D2}:{3:D2}",
            days,
            uptime.Hours,
            uptime.Minutes,
            uptime.Seconds);
    }

    public static string MaskHostname(string hostName, bool maskSensitive)
    {
        if (!maskSensitive || string.IsNullOrWhiteSpace(hostName))
            return hostName;
        int dash = hostName.IndexOf('-');
        if (dash > 0 && dash < hostName.Length - 1)
            return string.Concat(hostName.AsSpan(0, dash + 1), "****");
        if (hostName.Length > 3)
            return string.Concat(hostName.AsSpan(0, 3), "-****");
        return "HOST-****";
    }

    public static string MaskUsername(string userName, bool maskSensitive)
    {
        if (!maskSensitive || string.IsNullOrWhiteSpace(userName))
            return userName;
        return "****";
    }

    public static string MaskMacAddress(string mac, bool maskSensitive)
    {
        if (!maskSensitive || string.IsNullOrWhiteSpace(mac))
            return mac;
        string[] parts = mac.Split([':', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 6)
            return $"{parts[0]}:{parts[1]}:{parts[2]}:xx:xx:xx";
        return "xx:xx:xx:xx:xx:xx";
    }

    public static string MaskIpv4OrEndpoint(string addressOrEndpoint, bool maskSensitive)
    {
        if (!maskSensitive || string.IsNullOrWhiteSpace(addressOrEndpoint))
            return addressOrEndpoint;

        string addr = addressOrEndpoint;
        string portSuffix = string.Empty;
        int colonIdx = addressOrEndpoint.LastIndexOf(':');
        if (colonIdx > 0 && addressOrEndpoint.IndexOf(':') == colonIdx)
        {
            addr = addressOrEndpoint[..colonIdx];
            portSuffix = addressOrEndpoint[colonIdx..];
        }

        string[] octets = addr.Split('.');
        if (octets.Length == 4)
        {
            return $"{octets[0]}.{octets[1]}.x.x{portSuffix}";
        }

        return addressOrEndpoint;
    }

    public static string MaskPath(string path, bool maskSensitive)
    {
        if (!maskSensitive || string.IsNullOrWhiteSpace(path))
            return path;

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile) && path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(@"C:\Users\****", path.AsSpan(userProfile.Length));
        }

        string userName = Environment.UserName;
        if (!string.IsNullOrEmpty(userName) && path.Contains(userName, StringComparison.OrdinalIgnoreCase))
        {
            return path.Replace(userName, "****", StringComparison.OrdinalIgnoreCase);
        }

        return path;
    }

    public static string FormatHexLineBytes(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = stackalloc char[49];
        int charIdx = 0;
        for (int i = 0; i < 16; i++)
        {
            if (i < bytes.Length)
            {
                byte b = bytes[i];
                chars[charIdx++] = GetHexNibble(b >> 4);
                chars[charIdx++] = GetHexNibble(b & 0xF);
            }
            else
            {
                chars[charIdx++] = ' ';
                chars[charIdx++] = ' ';
            }

            if (i < 15)
            {
                chars[charIdx++] = ' ';
                if (i == 7)
                {
                    chars[charIdx++] = ' ';
                }
            }
        }
        return new string(chars);

        static char GetHexNibble(int val) => val < 10 ? (char)('0' + val) : (char)('A' + (val - 10));
    }

    public static string FormatAsciiGutter(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = stackalloc char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            chars[i] = (b >= 32 && b <= 126) ? (char)b : '.';
        }
        return new string(chars);
    }
}
