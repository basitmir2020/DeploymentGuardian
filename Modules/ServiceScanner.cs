using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class ServiceScanner : IServiceDataCollector
{
    private readonly IShellHelper _shell;

    /// <summary>
    /// Creates a service scanner with shell command execution support.
    /// </summary>
    public ServiceScanner(IShellHelper shell)
    {
        _shell = shell;
    }

    /// <summary>
    /// Lists currently running systemd services.
    /// </summary>
    public ServiceReport Scan()
    {
        var result = _shell.RunCommand("systemctl list-units --type=service --state=running --no-legend");
        if (!result.Succeeded)
        {
            return new ServiceReport();
        }

        var services = result.StdOut.Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length > 0)
            .Select(parts => parts[0])
            .ToList();

        return new ServiceReport
        {
            RunningServices = services
        };
    }
}
