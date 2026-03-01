using System.Globalization;

namespace DeploymentGuardian.Modules;

public class MemoryTrendAnalyzer
{
    private readonly string _logFilePath;
    private readonly object _fileLock = new();

    /// <summary>
    /// Creates a memory trend analyzer that stores samples in the given log path.
    /// </summary>
    public MemoryTrendAnalyzer(string logFilePath)
    {
        _logFilePath = logFilePath;
    }

    /// <summary>
    /// Persists the latest memory sample and detects monotonic growth over recent samples.
    /// </summary>
    public bool IsMemoryIncreasing(double current, int sampleCount, double alertThresholdPercent)
    {
        if (sampleCount < 2)
        {
            return false;
        }

        lock (_fileLock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(
                    _logFilePath,
                    $"{DateTime.UtcNow:O},{current.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}");

                var values = File.ReadAllLines(_logFilePath)
                    .TakeLast(sampleCount)
                    .Select(ParseMemoryValue)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                return values.Count == sampleCount &&
                       values.SequenceEqual(values.OrderBy(x => x)) &&
                       values.Last() > alertThresholdPercent;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Parses a memory sample line formatted as ISO8601 timestamp,value.
    /// </summary>
    private static double? ParseMemoryValue(string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 2)
        {
            return null;
        }

        if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
