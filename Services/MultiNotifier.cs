using DeploymentGuardian.Abstractions;

namespace DeploymentGuardian.Services;

public class MultiNotifier : INotifier
{
    private readonly IReadOnlyList<INotifier> _notifiers;

    /// <summary>
    /// Creates a notifier that fan-outs each message to multiple channels.
    /// </summary>
    public MultiNotifier(IEnumerable<INotifier> notifiers)
    {
        ArgumentNullException.ThrowIfNull(notifiers);

        _notifiers = notifiers.Where(n => n is not null).ToList();
        if (_notifiers.Count == 0)
        {
            throw new ArgumentException("At least one notifier is required.", nameof(notifiers));
        }
    }

    /// <summary>
    /// Sends to all channels and returns success only when all channels succeed.
    /// </summary>
    public async Task SendAsync(string message)
    {
        var errors = new List<Exception>();

        foreach (var notifier in _notifiers)
        {
            try
            {
                await notifier.SendAsync(message);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("One or more notification channels failed.", errors);
        }
    }
}
