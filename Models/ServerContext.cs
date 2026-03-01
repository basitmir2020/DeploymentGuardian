namespace DeploymentGuardian.Models;

public class ServerContext
{
    public required ServerMetrics Metrics { get; set; }
    public required List<ProcessInfo> Processes { get; set; }
    public required SecurityReport Security { get; set; }
    public required ServiceReport Services { get; set; }
}
