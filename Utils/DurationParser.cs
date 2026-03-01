using System.Globalization;

namespace DeploymentGuardian.Utils;

public static class DurationParser
{
    /// <summary>
    /// Parses duration strings like 30s, 5m, 1h, 1d, or plain seconds.
    /// </summary>
    public static bool TryParse(string raw, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var value = raw.Trim().ToLowerInvariant();

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            seconds >= 0)
        {
            duration = TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (value.Length < 2)
        {
            return false;
        }

        var unit = value[^1];
        var numberPart = value[..^1];

        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            number < 0)
        {
            return false;
        }

        duration = unit switch
        {
            's' => TimeSpan.FromSeconds(number),
            'm' => TimeSpan.FromMinutes(number),
            'h' => TimeSpan.FromHours(number),
            'd' => TimeSpan.FromDays(number),
            _ => TimeSpan.Zero
        };

        return duration > TimeSpan.Zero;
    }
}
