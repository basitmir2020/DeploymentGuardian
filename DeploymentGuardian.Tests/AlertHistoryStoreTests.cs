using DeploymentGuardian.Models;
using DeploymentGuardian.Modules;

namespace DeploymentGuardian.Tests;

public class AlertHistoryStoreTests
{
    [Fact]
    public void Append_TrimsEntriesToConfiguredMax()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"guardian-history-{Guid.NewGuid():N}.jsonl");
        try
        {
            var store = new AlertHistoryStore(filePath, 3);

            for (var i = 0; i < 5; i++)
            {
                store.Append(new AlertHistoryEntry
                {
                    TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(i),
                    RiskScore = i,
                    AlertCodes = new List<string> { $"CODE_{i}" }
                });
            }

            var lines = File.ReadAllLines(filePath);
            Assert.Equal(3, lines.Length);
            Assert.Contains("CODE_2", lines[0]);
            Assert.Contains("CODE_3", lines[1]);
            Assert.Contains("CODE_4", lines[2]);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
