using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Services;

namespace DeploymentGuardian.Tests;

public class MultiNotifierTests
{
    [Fact]
    public async Task SendAsync_SendsToAllNotifiers()
    {
        var first = new FakeNotifier();
        var second = new FakeNotifier();
        var notifier = new MultiNotifier(new INotifier[] { first, second });

        await notifier.SendAsync("test-message");

        Assert.Equal(1, first.SendCount);
        Assert.Equal(1, second.SendCount);
    }

    [Fact]
    public async Task SendAsync_WhenOneFails_ThrowsAggregateException()
    {
        var healthy = new FakeNotifier();
        var failing = new FakeNotifier(shouldThrow: true);
        var notifier = new MultiNotifier(new INotifier[] { healthy, failing });

        var exception = await Assert.ThrowsAsync<AggregateException>(() => notifier.SendAsync("test-message"));

        Assert.Single(exception.InnerExceptions);
        Assert.Equal(1, healthy.SendCount);
        Assert.Equal(1, failing.SendCount);
    }

    private sealed class FakeNotifier : INotifier
    {
        private readonly bool _shouldThrow;

        public int SendCount { get; private set; }

        public FakeNotifier(bool shouldThrow = false)
        {
            _shouldThrow = shouldThrow;
        }

        public Task SendAsync(string message)
        {
            SendCount++;
            if (_shouldThrow)
            {
                throw new InvalidOperationException("Simulated send failure.");
            }

            return Task.CompletedTask;
        }
    }
}
