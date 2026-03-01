using System.Diagnostics;
using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class WindowsProcessCollector : IProcessDataCollector
{
    /// <summary>
    /// Collects top Windows processes with estimated CPU and memory usage percentages.
    /// </summary>
    public List<ProcessInfo> GetTopProcesses()
    {
        try
        {
            var first = CaptureSnapshot();
            var start = Stopwatch.StartNew();
            Thread.Sleep(300);
            var second = CaptureSnapshot();
            start.Stop();

            if (start.Elapsed.TotalMilliseconds <= 0 || Environment.ProcessorCount <= 0)
            {
                return new List<ProcessInfo>();
            }

            var totalMemoryBytes = GetTotalMemoryBytes();
            var data = new List<ProcessInfo>();

            foreach (var current in second)
            {
                if (!first.TryGetValue(current.Key, out var previous))
                {
                    continue;
                }

                var cpuDeltaMs = current.Value.CpuMs - previous.CpuMs;
                var cpuPercent = cpuDeltaMs / (Environment.ProcessorCount * start.Elapsed.TotalMilliseconds) * 100.0;
                var memoryPercent = totalMemoryBytes <= 0
                    ? 0
                    : current.Value.WorkingSetBytes / totalMemoryBytes * 100.0;

                data.Add(new ProcessInfo
                {
                    Name = current.Value.Name,
                    Cpu = Math.Max(0, cpuPercent),
                    Memory = Math.Max(0, memoryPercent)
                });
            }

            return data
                .OrderByDescending(p => p.Cpu)
                .Take(5)
                .ToList();
        }
        catch
        {
            return new List<ProcessInfo>();
        }
    }

    /// <summary>
    /// Captures process CPU time and working-set memory snapshot.
    /// </summary>
    private static Dictionary<int, ProcessSnapshot> CaptureSnapshot()
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
                // Ignore restricted or terminated processes.
            }
            finally
            {
                process.Dispose();
            }
        }

        return snapshots;
    }

    /// <summary>
    /// Reads total physical memory bytes on Windows.
    /// </summary>
    private static double GetTotalMemoryBytes()
    {
        return WindowsCollectorUtils.TryGetPhysicalMemoryBytes(out var total, out _)
            ? total
            : 0;
    }

    private readonly record struct ProcessSnapshot(string Name, double CpuMs, double WorkingSetBytes);
}
