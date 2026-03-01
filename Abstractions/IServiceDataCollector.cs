using DeploymentGuardian.Models;

namespace DeploymentGuardian.Abstractions;

public interface IServiceDataCollector
{
    /// <summary>
    /// Collects running service data from the current host.
    /// </summary>
    ServiceReport Scan();
}
