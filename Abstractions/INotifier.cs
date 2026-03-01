namespace DeploymentGuardian.Abstractions;

public interface INotifier
{
    /// <summary>
    /// Sends a user-facing alert message to a notification channel.
    /// </summary>
    Task SendAsync(string message);
}
