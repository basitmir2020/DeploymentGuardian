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
    private static readonly TimeSpan ProcessWindowCacheTtl = TimeSpan.FromSeconds(1);
    private static readonly object ProcessWindowCacheLock = new();
    private static DateTimeOffset _processWindowCachedAtUtc = DateTimeOffset.MinValue;
    private static ProcessCpuWindow _cachedProcessWindow;
    private static bool _hasProcessWindowCache;

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

    /// <summary>
    /// Captures two short-interval process snapshots used by Windows CPU collectors.
    /// </summary>
    public static bool TryGetProcessCpuWindow(out ProcessCpuWindow window)
    {
        window = default;

        var now = DateTimeOffset.UtcNow;
        if (_hasProcessWindowCache && now - _processWindowCachedAtUtc < ProcessWindowCacheTtl)
        {
            window = _cachedProcessWindow;
            return window.ElapsedMilliseconds > 0;
        }

        lock (ProcessWindowCacheLock)
        {
            now = DateTimeOffset.UtcNow;
            if (_hasProcessWindowCache && now - _processWindowCachedAtUtc < ProcessWindowCacheTtl)
            {
                window = _cachedProcessWindow;
                return window.ElapsedMilliseconds > 0;
            }

            if (!BuildProcessCpuWindow(out window))
            {
                return false;
            }

            _cachedProcessWindow = window;
            _processWindowCachedAtUtc = now;
            _hasProcessWindowCache = true;
            return true;
        }
    }

    private static bool BuildProcessCpuWindow(out ProcessCpuWindow window)
    {
        window = default;

        var first = CaptureProcessSnapshot();
        var stopwatch = Stopwatch.StartNew();
        Thread.Sleep(300);
        var second = CaptureProcessSnapshot();
        stopwatch.Stop();

        if (stopwatch.Elapsed.TotalMilliseconds <= 0 || Environment.ProcessorCount <= 0)
        {
            return false;
        }

        var totalDelta = 0.0;
        foreach (var current in second)
        {
            if (!first.TryGetValue(current.Key, out var previous))
            {
                continue;
            }

            totalDelta += Math.Max(0, current.Value.CpuMilliseconds - previous.CpuMilliseconds);
        }

        window = new ProcessCpuWindow(
            stopwatch.Elapsed.TotalMilliseconds,
            totalDelta,
            first,
            second);
        return true;
    }

    private static Dictionary<int, ProcessSnapshot> CaptureProcessSnapshot()
    {
        var snapshots = new Dictionary<int, ProcessSnapshot>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                snapshots[process.Id] = new ProcessSnapshot(
                    process.ProcessName,
                    process.TotalProcessorTime.TotalMilliseconds,
                    process.WorkingSet64);
            }
            catch
            {
                // Ignore restricted or short-lived processes.
            }
            finally
            {
                process.Dispose();
            }
        }

        return snapshots;
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

    internal readonly record struct ProcessSnapshot(string Name, double CpuMilliseconds, double WorkingSetBytes);

    internal readonly record struct ProcessCpuWindow(
        double ElapsedMilliseconds,
        double TotalCpuDeltaMilliseconds,
        Dictionary<int, ProcessSnapshot> FirstSnapshots,
        Dictionary<int, ProcessSnapshot> SecondSnapshots);
}
