using DeploymentGuardian.Models;
using DeploymentGuardian.Modules;

namespace DeploymentGuardian.Tests;

public class AlertDeduplicatorTests
{
    [Fact]
    public void FilterSendable_RespectsCooldownByCodeAndTarget()
    {
        var stateFile = Path.Combine(Path.GetTempPath(), $"guardian-dedup-{Guid.NewGuid():N}.json");
        try
        {
            var deduplicator = new AlertDeduplicator(stateFile, TimeSpan.FromMinutes(30));
            var now = DateTimeOffset.UtcNow;

            var alert = new Alert
            {
                Code = AlertCodes.ProcessCpuHigh,
                Target = "dotnet",
                Message = "High CPU process detected: dotnet at 90%.",
                Severity = AlertSeverity.Critical
            };

            var firstPass = deduplicator.FilterSendable(new[] { alert }, now);
            Assert.Single(firstPass);

            deduplicator.MarkSent(firstPass, now);

            var secondPass = deduplicator.FilterSendable(new[] { alert }, now.AddMinutes(5));
            Assert.Empty(secondPass);

            var thirdPass = deduplicator.FilterSendable(new[] { alert }, now.AddMinutes(31));
            Assert.Single(thirdPass);
        }
        finally
        {
            if (File.Exists(stateFile))
            {
                File.Delete(stateFile);
            }
        }
    }
}
