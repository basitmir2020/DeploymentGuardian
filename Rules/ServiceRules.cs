using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Rules;

public class ServiceRules : IRule<ServerContext>
{
    /// <summary>
    /// Flags service state issues from systemd service scan results.
    /// </summary>
    public List<Alert> Evaluate(ServerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var alerts = new List<Alert>();
        var services = context.Services.RunningServices;

        if (!services.Any())
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.ServicesNoneRunning,
                Message = "No running services detected.",
                Severity = AlertSeverity.Critical
            });

            return alerts;
        }

        var hasSshService = services.Any(s =>
            string.Equals(s, "ssh.service", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "sshd.service", StringComparison.OrdinalIgnoreCase));

        if (!hasSshService)
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.SshServiceMissing,
                Message = "SSH service was not found in running services.",
                Severity = AlertSeverity.Warning
            });
        }

        return alerts;
    }
}
