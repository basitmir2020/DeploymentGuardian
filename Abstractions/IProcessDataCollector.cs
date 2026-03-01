using DeploymentGuardian.Models;

namespace DeploymentGuardian.Abstractions;

public interface IProcessDataCollector
{
    /// <summary>
    /// Collects high-resource process data from the current host.
    /// </summary>
    List<ProcessInfo> GetTopProcesses();
}
