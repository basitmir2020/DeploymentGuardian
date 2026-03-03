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
        var sendTasks = _notifiers.Select(async notifier =>
        {
            try
            {
                await notifier.SendAsync(message);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var taskResults = await Task.WhenAll(sendTasks);
        var errors = taskResults
            .Where(static ex => ex is not null)
            .Cast<Exception>()
            .ToList();

        if (errors.Count > 0)
        {
            throw new AggregateException("One or more notification channels failed.", errors);
        }
    }

    public async Task<string?> SendTrackedAsync(string message)
    {
        var sendTasks = _notifiers.Select(async notifier =>
        {
            try
            {
                var id = await notifier.SendTrackedAsync(message);
                return (Exception: (Exception?)null, Id: id);
            }
            catch (Exception ex)
            {
                return (Exception: ex, Id: (string?)null);
            }
        });

        var taskResults = await Task.WhenAll(sendTasks);
        var errors = taskResults
            .Where(r => r.Exception is not null)
            .Select(r => r.Exception!)
            .ToList();

        if (errors.Count > 0)
        {
            throw new AggregateException("One or more notification channels failed.", errors);
        }

        return taskResults.FirstOrDefault(r => r.Id is not null).Id;
    }

    public async Task EditTrackedAsync(string trackingId, string message)
    {
        var sendTasks = _notifiers.Select(async notifier =>
        {
            try
            {
                await notifier.EditTrackedAsync(trackingId, message);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var taskResults = await Task.WhenAll(sendTasks);
        var errors = taskResults
            .Where(static ex => ex is not null)
            .Cast<Exception>()
            .ToList();

        if (errors.Count > 0)
        {
            throw new AggregateException("One or more notification channels failed.", errors);
        }
    }
}
