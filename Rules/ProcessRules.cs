using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Rules;

public class ProcessRules : IRule<ServerContext>
{
    private readonly double _processCpuCriticalPercent;
    private readonly double _processMemoryWarningPercent;

    /// <summary>
    /// Creates process health rules with CPU and memory thresholds.
    /// </summary>
    public ProcessRules(double processCpuCriticalPercent, double processMemoryWarningPercent)
    {
        _processCpuCriticalPercent = processCpuCriticalPercent;
        _processMemoryWarningPercent = processMemoryWarningPercent;
    }

    /// <summary>
    /// Flags hotspot processes based on CPU and memory usage.
    /// </summary>
    public List<Alert> Evaluate(ServerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var alerts = new List<Alert>();
        if (!context.Processes.Any())
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.ProcessDataMissing,
                Message = "No process data was collected.",
                Severity = AlertSeverity.Warning
            });

            return alerts;
        }

        foreach (var process in context.Processes.Where(p => p.Cpu >= _processCpuCriticalPercent).Take(3))
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.ProcessCpuHigh,
                Target = process.Name,
                Message = $"High CPU process detected: {process.Name} at {process.Cpu:F1}%.",
                Severity = AlertSeverity.Critical
            });
        }

        foreach (var process in context.Processes.Where(p => p.Memory >= _processMemoryWarningPercent).Take(3))
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.ProcessMemoryHigh,
                Target = process.Name,
                Message = $"High memory process detected: {process.Name} at {process.Memory:F1}%.",
                Severity = AlertSeverity.Warning
            });
        }

        return alerts;
    }
}
