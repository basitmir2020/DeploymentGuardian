namespace DeploymentGuardian.Abstractions;

public interface IAiAdvisor
{
    /// <summary>
    /// Generates mitigation suggestions from a compact monitoring summary.
    /// </summary>
    Task<string> GetSuggestionsAsync(string summary);

    /// <summary>
    /// Generates mitigation suggestions from a compact monitoring summary, streaming the response.
    /// </summary>
    IAsyncEnumerable<string> GetSuggestionsStreamAsync(string summary, CancellationToken cancellationToken = default);
    /// <summary>
    /// Generates exact shell commands or actionable steps to implement a set of given suggestions.
    /// </summary>
    Task<string> GetImplementationStepsAsync(string suggestions);
    IAsyncEnumerable<string> GetImplementationStepsStreamAsync(string suggestions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a server-specific security audit based on a security summary prompt.
    /// </summary>
    Task<string> GetSecuritySuggestionsAsync(string securitySummary);
    IAsyncEnumerable<string> GetSecuritySuggestionsStreamAsync(string securitySummary, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates performance tuning advice based on a hardware metrics summary to maximize potential without crashing.
    /// </summary>
    Task<string> GetPerformanceTuningAsync(string metricsSummary);
    IAsyncEnumerable<string> GetPerformanceTuningStreamAsync(string metricsSummary, CancellationToken cancellationToken = default);
}
