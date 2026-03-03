using System.Diagnostics;
using System.Globalization;

namespace DeploymentGuardian.Modules;

internal static class WindowsCollectorUtils
{
    private static readonly TimeSpan MemoryCacheTtl = TimeSpan.FromSeconds(2);
    private static readonly object MemoryCacheLock = new();
    private static DateTimeOffset _memoryCachedAtUtc = DateTimeOffset.MinValue;
    private static double _cachedTotalBytes;
    private static double _cachedFreeBytes;
    private static bool _hasMemoryCache;

    /// <summary>
    /// Attempts to read total/free physical memory bytes from Win32_OperatingSystem.
    /// </summary>
    public static bool TryGetPhysicalMemoryBytes(out double totalBytes, out double freeBytes)
    {
        totalBytes = 0;
        freeBytes = 0;

        var now = DateTimeOffset.UtcNow;
        if (_hasMemoryCache && now - _memoryCachedAtUtc < MemoryCacheTtl)
        {
            totalBytes = _cachedTotalBytes;
            freeBytes = _cachedFreeBytes;
            return totalBytes > 0;
        }

        lock (MemoryCacheLock)
        {
            now = DateTimeOffset.UtcNow;
            if (_hasMemoryCache && now - _memoryCachedAtUtc < MemoryCacheTtl)
            {
                totalBytes = _cachedTotalBytes;
                freeBytes = _cachedFreeBytes;
                return totalBytes > 0;
            }

            if (!ReadPhysicalMemoryBytes(out totalBytes, out freeBytes))
            {
                return false;
            }

            _cachedTotalBytes = totalBytes;
            _cachedFreeBytes = freeBytes;
            _memoryCachedAtUtc = now;
            _hasMemoryCache = true;
            return true;
        }
    }

    private static bool ReadPhysicalMemoryBytes(out double totalBytes, out double freeBytes)
    {
        totalBytes = 0;
        freeBytes = 0;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"$os=Get-CimInstance Win32_OperatingSystem; Write-Output \\\"$($os.TotalVisibleMemorySize)|$($os.FreePhysicalMemory)\\\"\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(5000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore kill failures on timeout.
                }

                return false;
            }

            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            var parts = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                return false;
            }

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var totalKb) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var freeKb))
            {
                return false;
            }

            totalBytes = totalKb * 1024.0;
            freeBytes = freeKb * 1024.0;
            return totalBytes > 0;
        }
        catch
        {
            return false;
        }
    }
}
