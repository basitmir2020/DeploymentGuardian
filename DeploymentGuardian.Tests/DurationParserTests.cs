using DeploymentGuardian.Utils;

namespace DeploymentGuardian.Tests;

public class DurationParserTests
{
    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("2h", 7200)]
    [InlineData("1d", 86400)]
    [InlineData("45", 45)]
    public void TryParse_ValidValues_ReturnsExpectedSeconds(string raw, int expectedSeconds)
    {
        var parsed = DurationParser.TryParse(raw, out var duration);

        Assert.True(parsed);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), duration);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5m")]
    [InlineData("7x")]
    [InlineData("m")]
    public void TryParse_InvalidValues_ReturnsFalse(string raw)
    {
        var parsed = DurationParser.TryParse(raw, out _);

        Assert.False(parsed);
    }
}
