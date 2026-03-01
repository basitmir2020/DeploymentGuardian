using System.Globalization;
using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class ServerAnalyzer : IServerDataCollector
{
    private readonly IShellHelper _shell;

    /// <summary>
    /// Creates a server analyzer with shell command execution support.
    /// </summary>
    public ServerAnalyzer(IShellHelper shell)
    {
        _shell = shell;
    }

    /// <summary>
    /// Collects core server metrics and returns safe defaults when parsing fails.
    /// </summary>
    public ServerMetrics Analyze()
    {
        var swapResult = _shell.RunCommand("swapon --show");

        return new ServerMetrics
        {
            CpuLoad = GetCpuLoad(),
            CpuCores = GetCpuCores(),
            RamUsagePercent = GetRamUsage(),
            DiskUsagePercent = GetDiskUsage(),
            SwapEnabled = swapResult.Succeeded && !string.IsNullOrWhiteSpace(swapResult.StdOut)
        };
    }

    /// <summary>
    /// Reads the one-minute load average from /proc/loadavg.
    /// </summary>
    private double GetCpuLoad()
    {
        var result = _shell.RunCommand("cat /proc/loadavg");
        if (!result.Succeeded)
        {
            return 0;
        }

        var firstToken = result.StdOut
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return TryParseDouble(firstToken, out var value) ? value : 0;
    }

    /// <summary>
    /// Reads CPU core count using nproc, with runtime fallback.
    /// </summary>
    private int GetCpuCores()
    {
        var result = _shell.RunCommand("nproc");
        if (!result.Succeeded)
        {
            return Environment.ProcessorCount;
        }

        return int.TryParse(result.StdOut, out var cores) && cores > 0
            ? cores
            : Environment.ProcessorCount;
    }

    /// <summary>
    /// Computes RAM utilization percentage from free command output.
    /// </summary>
    private double GetRamUsage()
    {
        var result = _shell.RunCommand("free -m");
        if (!result.Succeeded)
        {
            return 0;
        }

        var line = result.StdOut.Split('\n').FirstOrDefault(l => l.StartsWith("Mem:", StringComparison.Ordinal));
        if (line is null)
        {
            return 0;
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 ||
            !TryParseDouble(parts[1], out var total) ||
            !TryParseDouble(parts[2], out var used) ||
            total <= 0)
        {
            return 0;
        }

        return (used / total) * 100;
    }

    /// <summary>
    /// Computes root filesystem usage percentage from df output.
    /// </summary>
    private double GetDiskUsage()
    {
        var result = _shell.RunCommand("df -h /");
        if (!result.Succeeded)
        {
            return 0;
        }

        var line = result.StdOut.Split('\n').Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(line))
        {
            return 0;
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
        {
            return 0;
        }

        var percent = parts[4].Replace("%", "", StringComparison.Ordinal);
        return TryParseDouble(percent, out var usage) ? usage : 0;
    }

    /// <summary>
    /// Parses floating-point values using invariant culture.
    /// </summary>
    private static bool TryParseDouble(string? value, out double parsed)
    {
        return double.TryParse(
            value,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out parsed);
    }
}
