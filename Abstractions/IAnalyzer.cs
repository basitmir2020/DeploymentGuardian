namespace DeploymentGuardian.Abstractions;

public interface IAnalyzer<T>
{
    /// <summary>
    /// Produces an analysis snapshot for the requested type.
    /// </summary>
    T Analyze();
}
