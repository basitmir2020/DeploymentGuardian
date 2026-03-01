using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Rules;

public class SecurityRules : IRule<ServerContext>
{
    private readonly int _maxOpenPortsWarningCount;

    /// <summary>
    /// Creates security rules with configurable open-port warning threshold.
    /// </summary>
    public SecurityRules(int maxOpenPortsWarningCount)
    {
        _maxOpenPortsWarningCount = maxOpenPortsWarningCount;
    }

    /// <summary>
    /// Flags critical security posture issues from scanner output.
    /// </summary>
    public List<Alert> Evaluate(ServerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var alerts = new List<Alert>();

        if (!context.Security.FirewallEnabled)
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.FirewallDisabled,
                Message = "Firewall is disabled.",
                Severity = AlertSeverity.Warning
            });
        }

        if (!context.Security.RootLoginDisabled)
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.RootLoginEnabled,
                Message = "Root SSH login enabled.",
                Severity = AlertSeverity.Critical
            });
        }

        if (!context.Security.PasswordAuthDisabled)
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.SshPasswordAuthEnabled,
                Message = "SSH password authentication is enabled.",
                Severity = AlertSeverity.Warning
            });
        }

        if (!context.Security.Fail2BanInstalled)
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.Fail2BanInactive,
                Message = "Fail2Ban is not active.",
                Severity = AlertSeverity.Warning
            });
        }

        if (context.Security.OpenPorts.Count > _maxOpenPortsWarningCount)
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.OpenPortsHigh,
                Message = $"Open ports exceed {_maxOpenPortsWarningCount} ({context.Security.OpenPorts.Count} found).",
                Severity = AlertSeverity.Warning
            });
        }

        return alerts;
    }
}
