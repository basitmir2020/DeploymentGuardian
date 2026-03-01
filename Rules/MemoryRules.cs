using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Rules;

public class MemoryRules : IRule<ServerContext>
{
    private readonly double _ramUsageWarningPercent;

    /// <summary>
    /// Creates memory rules with configured RAM warning threshold.
    /// </summary>
    public MemoryRules(double ramUsageWarningPercent)
    {
        _ramUsageWarningPercent = ramUsageWarningPercent;
    }

    /// <summary>
    /// Flags high RAM consumption against configured warning threshold.
    /// </summary>
    public List<Alert> Evaluate(ServerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var alerts = new List<Alert>();

        if (context.Metrics.RamUsagePercent > _ramUsageWarningPercent)
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.RamUsageHigh,
                Message = "High RAM usage detected.",
                Severity = AlertSeverity.Warning
            });
        }

        return alerts;
    }
}
