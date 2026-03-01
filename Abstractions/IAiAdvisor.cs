namespace DeploymentGuardian.Abstractions;

public interface IAiAdvisor
{
    /// <summary>
    /// Generates mitigation suggestions from a compact monitoring summary.
    /// </summary>
    Task<string> GetSuggestionsAsync(string summary);
}
