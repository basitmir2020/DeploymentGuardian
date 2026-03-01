using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Rules;

public class ScalingRules : IRule<ServerContext>
{
    private readonly double _cpuSpikeMultiplier;
    private readonly double _diskUsageWarningPercent;

    /// <summary>
    /// Creates scaling rules with configured CPU and disk thresholds.
    /// </summary>
    public ScalingRules(double cpuSpikeMultiplier, double diskUsageWarningPercent)
    {
        _cpuSpikeMultiplier = cpuSpikeMultiplier;
        _diskUsageWarningPercent = diskUsageWarningPercent;
    }

    /// <summary>
    /// Flags CPU and disk thresholds that indicate scaling pressure.
    /// </summary>
    public List<Alert> Evaluate(ServerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var alerts = new List<Alert>();

        if (context.Metrics.CpuLoad > context.Metrics.CpuCores * _cpuSpikeMultiplier)
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.CpuSpike,
                Message = "CPU spike detected.",
                Severity = AlertSeverity.Critical
            });
        }

        if (context.Metrics.DiskUsagePercent > _diskUsageWarningPercent)
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.DiskUsageHigh,
                Message = $"Disk usage above {_diskUsageWarningPercent}%.",
                Severity = AlertSeverity.Warning
            });
        }

        return alerts;
    }
}
