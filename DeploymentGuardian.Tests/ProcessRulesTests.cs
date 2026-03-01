using DeploymentGuardian.Models;
using DeploymentGuardian.Rules;

namespace DeploymentGuardian.Tests;

public class ProcessRulesTests
{
    [Fact]
    public void Evaluate_HighCpuAndMemory_ProducesCodedAlerts()
    {
        var rules = new ProcessRules(processCpuCriticalPercent: 80, processMemoryWarningPercent: 25);
        var context = new ServerContext
        {
            Metrics = new ServerMetrics(),
            Security = new SecurityReport(),
            Services = new ServiceReport(),
            Processes =
            [
                new ProcessInfo { Name = "dotnet", Cpu = 93.5, Memory = 40.2 },
                new ProcessInfo { Name = "nginx", Cpu = 10.1, Memory = 2.4 }
            ]
        };

        var alerts = rules.Evaluate(context);

        Assert.Contains(alerts, a => a.Code == AlertCodes.ProcessCpuHigh && a.Target == "dotnet");
        Assert.Contains(alerts, a => a.Code == AlertCodes.ProcessMemoryHigh && a.Target == "dotnet");
    }
}
