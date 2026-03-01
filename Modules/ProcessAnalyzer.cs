using System.Globalization;
using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class ProcessAnalyzer : IProcessDataCollector
{
    private readonly IShellHelper _shell;

    /// <summary>
    /// Creates a process analyzer with shell command execution support.
    /// </summary>
    public ProcessAnalyzer(IShellHelper shell)
    {
        _shell = shell;
    }

    /// <summary>
    /// Returns top processes sorted by CPU usage and skips malformed rows.
    /// </summary>
    public List<ProcessInfo> GetTopProcesses()
    {
        var result = _shell.RunCommand("ps -eo comm,%cpu,%mem --sort=-%cpu | head -6");
        if (!result.Succeeded)
        {
            return new List<ProcessInfo>();
        }

        var lines = result.StdOut.Split('\n').Skip(1);
        var processes = new List<ProcessInfo>();

        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu))
            {
                continue;
            }

            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var memory))
            {
                continue;
            }

            processes.Add(new ProcessInfo
            {
                Name = parts[0],
                Cpu = cpu,
                Memory = memory
            });
        }

        return processes;
    }
}
