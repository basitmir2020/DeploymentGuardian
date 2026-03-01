using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class RiskCalculator
{
    private readonly double _riskRamPercent;
    private readonly int _maxOpenPortsWarningCount;
    private readonly double _processCpuCriticalPercent;

    /// <summary>
    /// Creates a risk calculator with configurable RAM, open-port, and process thresholds.
    /// </summary>
    public RiskCalculator(
        double riskRamPercent,
        int maxOpenPortsWarningCount,
        double processCpuCriticalPercent)
    {
        _riskRamPercent = riskRamPercent;
        _maxOpenPortsWarningCount = maxOpenPortsWarningCount;
        _processCpuCriticalPercent = processCpuCriticalPercent;
    }

    /// <summary>
    /// Calculates a bounded risk score from performance, process, and security signals.
    /// </summary>
    public int Calculate(ServerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var score = 0;

        if (context.Metrics.RamUsagePercent > _riskRamPercent) score += 20;
        if (context.Metrics.CpuLoad > context.Metrics.CpuCores) score += 20;

        if (!context.Security.FirewallEnabled) score += 15;
        if (!context.Security.RootLoginDisabled) score += 20;
        if (!context.Security.PasswordAuthDisabled) score += 10;
        if (!context.Security.Fail2BanInstalled) score += 5;
        if (context.Security.OpenPorts.Count > _maxOpenPortsWarningCount) score += 5;

        if (context.Processes.Any(p => p.Cpu >= _processCpuCriticalPercent)) score += 5;

        return Math.Min(score, 100);
    }
}
