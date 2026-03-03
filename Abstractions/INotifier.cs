namespace DeploymentGuardian.Abstractions;

public interface INotifier
{
    /// <summary>
    /// Sends a user-facing alert message to a notification channel.
    /// </summary>
    Task SendAsync(string message);

    /// <summary>
    /// Sends a user-facing alert message and returns a tracking ID if the channel supports editing.
    /// </summary>
    Task<string?> SendTrackedAsync(string message) => Task.FromResult<string?>(null);

    /// <summary>
    /// Edits an existing message by tracking ID.
    /// </summary>
    Task EditTrackedAsync(string trackingId, string message) => Task.CompletedTask;
}
