using System.Diagnostics;
using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class WindowsServerCollector : IServerDataCollector
{
    /// <summary>
    /// Collects server metrics on Windows hosts.
    /// </summary>
    public ServerMetrics Analyze()
    {
        var ram = ReadRamUsagePercent();
        return new ServerMetrics
        {
            CpuLoad = ReadCpuLoadPercent(),
            CpuCores = Environment.ProcessorCount,
            RamUsagePercent = ram.Percent,
            RamTotalMb = ram.TotalMb,
            DiskUsagePercent = ReadSystemDiskUsagePercent(),
            SwapEnabled = IsPageFilePresent()
        };
    }

    /// <summary>
    /// Estimates total CPU load by sampling cumulative process CPU time.
    /// </summary>
    private static double ReadCpuLoadPercent()
    {
        try
        {
            var firstSample = SnapshotTotalProcessCpuMs();
            var stopwatch = Stopwatch.StartNew();
            Thread.Sleep(300);
            var secondSample = SnapshotTotalProcessCpuMs();
            stopwatch.Stop();

            if (stopwatch.Elapsed.TotalMilliseconds <= 0 || Environment.ProcessorCount <= 0)
            {
                return 0;
            }

            var cpuDeltaMs = secondSample - firstSample;
            var usage = cpuDeltaMs / (Environment.ProcessorCount * stopwatch.Elapsed.TotalMilliseconds) * 100.0;
            return Math.Clamp(usage, 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Reads RAM usage percentage and total MB from Windows physical memory counters.
    /// </summary>
    private static (double Percent, double TotalMb) ReadRamUsagePercent()
    {
        if (!WindowsCollectorUtils.TryGetPhysicalMemoryBytes(out var total, out var free) || total <= 0)
        {
            return (0, 0);
        }

        var used = total - free;
        return (used / total * 100.0, total / 1024.0 / 1024.0);
    }

    /// <summary>
    /// Reads system drive utilization percentage.
    /// </summary>
    private static double ReadSystemDiskUsagePercent()
    {
        try
        {
            var systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
            var drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .ToList();

            var drive = drives.FirstOrDefault(d =>
                            string.Equals(d.Name.TrimEnd('\\'), systemDrive, StringComparison.OrdinalIgnoreCase))
                        ?? drives.FirstOrDefault();

            if (drive is null || drive.TotalSize <= 0)
            {
                return 0;
            }

            var used = drive.TotalSize - drive.AvailableFreeSpace;
            return used / (double)drive.TotalSize * 100.0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Checks if a Windows pagefile exists.
    /// </summary>
    private static bool IsPageFilePresent()
    {
        try
        {
            var systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
            var pageFile = $"{systemDrive}\\pagefile.sys";
            return File.Exists(pageFile);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Captures total CPU time consumed by all processes in milliseconds.
    /// </summary>
    private static double SnapshotTotalProcessCpuMs()
    {
        var total = 0.0;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                total += process.TotalProcessorTime.TotalMilliseconds;
            }
            catch
            {
                // Ignore short-lived or permission-restricted processes.
            }
            finally
            {
                process.Dispose();
            }
        }

        return total;
    }
}
