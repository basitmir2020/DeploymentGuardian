using System.Net;
using DeploymentGuardian.Abstractions;

namespace DeploymentGuardian.Services;

public class RetryingNotifier : INotifier
{
    private readonly INotifier _innerNotifier;
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseDelay;

    /// <summary>
    /// Creates a notifier wrapper that retries failed sends with exponential backoff.
    /// </summary>
    public RetryingNotifier(INotifier innerNotifier, int maxAttempts, TimeSpan baseDelay)
    {
        _innerNotifier = innerNotifier ?? throw new ArgumentNullException(nameof(innerNotifier));
        _maxAttempts = Math.Max(1, maxAttempts);
        _baseDelay = baseDelay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : baseDelay;
    }

    /// <summary>
    /// Sends a message and retries transient failures up to configured attempts.
    /// </summary>
    public Task SendAsync(string message) => ExecuteWithRetryAsync(async () => { await _innerNotifier.SendAsync(message); return true; });

    public Task<string?> SendTrackedAsync(string message) => ExecuteWithRetryAsync(() => _innerNotifier.SendTrackedAsync(message));

    public Task EditTrackedAsync(string trackingId, string message) => ExecuteWithRetryAsync(async () => { await _innerNotifier.EditTrackedAsync(trackingId, message); return true; });

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                lastError = ex;
                var isLastAttempt = attempt == _maxAttempts;
                var shouldRetry = !isLastAttempt && IsTransient(ex);
                if (!shouldRetry)
                {
                    break;
                }

                var delay = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay);
            }
        }

        throw new InvalidOperationException(
            $"Notification failed after {_maxAttempts} attempt(s).",
            lastError);
    }

    /// <summary>
    /// Determines whether an exception is likely transient and worth retrying.
    /// </summary>
    private static bool IsTransient(Exception exception)
    {
        if (exception is TimeoutException or TaskCanceledException)
        {
            return true;
        }

        if (exception is HttpRequestException httpException)
        {
            if (httpException.StatusCode is null)
            {
                return true;
            }

            return httpException.StatusCode == HttpStatusCode.TooManyRequests ||
                   (int)httpException.StatusCode >= 500;
        }

        return exception.InnerException is not null && IsTransient(exception.InnerException);
    }
}
