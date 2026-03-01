using DeploymentGuardian.Models;

namespace DeploymentGuardian.Abstractions;

public interface ISecurityDataCollector
{
    /// <summary>
    /// Collects security posture data from the current host.
    /// </summary>
    SecurityReport Analyze();
}
