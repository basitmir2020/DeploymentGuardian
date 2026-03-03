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
            if (!WindowsCollectorUtils.TryGetProcessCpuWindow(out var sample) || Environment.ProcessorCount <= 0)
            {
                return new List<ProcessInfo>();
            }

            var totalMemoryBytes = GetTotalMemoryBytes();
            var data = new List<ProcessInfo>(Math.Min(sample.SecondSnapshots.Count, 32));

            foreach (var current in sample.SecondSnapshots)
            {
                if (!sample.FirstSnapshots.TryGetValue(current.Key, out var previous))
                {
                    continue;
                }

                var cpuDeltaMs = current.Value.CpuMilliseconds - previous.CpuMilliseconds;
                var cpuPercent = cpuDeltaMs / (Environment.ProcessorCount * sample.ElapsedMilliseconds) * 100.0;
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
    /// Reads total physical memory bytes on Windows.
    /// </summary>
    private static double GetTotalMemoryBytes()
    {
        return WindowsCollectorUtils.TryGetPhysicalMemoryBytes(out var total, out _)
            ? total
            : 0;
    }
}
