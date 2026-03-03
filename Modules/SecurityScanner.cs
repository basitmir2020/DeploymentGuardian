using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class SecurityScanner : IAnalyzer<SecurityReport>, ISecurityDataCollector
{
    private readonly IShellHelper _shell;

    /// <summary>
    /// Creates a security scanner with shell command execution support.
    /// </summary>
    public SecurityScanner(IShellHelper shell)
    {
        _shell = shell;
    }

    /// <summary>
    /// Collects security controls and open port data from the host.
    /// </summary>
    public SecurityReport Analyze()
    {
        return new SecurityReport
        {
            FirewallEnabled = IsFirewallActive(),
            RootLoginDisabled = IsRootLoginDisabled(),
            PasswordAuthDisabled = IsPasswordAuthDisabled(),
            Fail2BanInstalled = IsFail2BanActive(),
            OpenPorts = GetOpenPorts()
        };
    }

    /// <summary>
    /// Checks whether UFW reports an active firewall.
    /// </summary>
    private bool IsFirewallActive()
    {
        var result = SafeRun("ufw status");
        return result.Contains("Status: active");
    }

    /// <summary>
    /// Checks SSH configuration for secure root login settings.
    /// </summary>
    private bool IsRootLoginDisabled()
    {
        var result = SafeRun("grep -Ei '^[[:space:]]*PermitRootLogin[[:space:]]+' /etc/ssh/sshd_config");
        var normalized = result.ToLowerInvariant();
        return normalized.Contains(" no") ||
               normalized.Contains(" prohibit-password") ||
               normalized.Contains(" without-password");
    }

    /// <summary>
    /// Checks SSH configuration for disabled password authentication.
    /// </summary>
    private bool IsPasswordAuthDisabled()
    {
        var result = SafeRun("grep -Ei '^[[:space:]]*PasswordAuthentication[[:space:]]+' /etc/ssh/sshd_config");
        return result.ToLowerInvariant().Contains(" no");
    }

    /// <summary>
    /// Checks whether fail2ban service is currently active.
    /// </summary>
    private bool IsFail2BanActive()
    {
        var result = SafeRun("systemctl is-active fail2ban");
        return string.Equals(result.Trim(), "active", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses distinct listening ports from ss command output.
    /// </summary>
    private List<int> GetOpenPorts()
    {
        var result = SafeRun("ss -H -ltnu");
        if (string.IsNullOrWhiteSpace(result))
        {
            return new List<int>();
        }

        var ports = new HashSet<int>();
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                continue;
            }

            var localAddress = parts[4];
            var separatorIndex = localAddress.LastIndexOf(':');
            if (separatorIndex < 0 || separatorIndex == localAddress.Length - 1)
            {
                continue;
            }

            var portToken = localAddress[(separatorIndex + 1)..];
            if (int.TryParse(portToken, out var port))
            {
                ports.Add(port);
            }
        }

        return ports.ToList();
    }

    /// <summary>
    /// Executes a shell command and shields scanner logic from command failures.
    /// </summary>
    private string SafeRun(string command)
    {
        try
        {
            var result = _shell.RunCommand(command);
            if (result.Succeeded)
            {
                return result.StdOut;
            }

            return string.IsNullOrWhiteSpace(result.StdOut) ? result.StdErr : result.StdOut;
        }
        catch
        {
            return string.Empty;
        }
    }
}
