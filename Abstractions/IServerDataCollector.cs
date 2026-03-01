using DeploymentGuardian.Models;

namespace DeploymentGuardian.Abstractions;

public interface IServerDataCollector
{
    /// <summary>
    /// Collects server-level metrics for the current host.
    /// </summary>
    ServerMetrics Analyze();
}
