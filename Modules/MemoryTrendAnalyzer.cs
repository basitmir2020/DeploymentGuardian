using System.Globalization;

namespace DeploymentGuardian.Modules;

public class MemoryTrendAnalyzer
{
    private readonly string _logFilePath;
    private readonly object _fileLock = new();
    private readonly Queue<double> _recentSamples = new();
    private int _loadedSampleCount;

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

                EnsureSamplesLoaded(sampleCount);

                File.AppendAllText(
                    _logFilePath,
                    $"{DateTime.UtcNow:O},{current.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}");

                _recentSamples.Enqueue(current);
                while (_recentSamples.Count > sampleCount)
                {
                    _recentSamples.Dequeue();
                }

                if (_recentSamples.Count != sampleCount)
                {
                    return false;
                }

                var previous = double.MinValue;
                foreach (var value in _recentSamples)
                {
                    if (value < previous)
                    {
                        return false;
                    }

                    previous = value;
                }

                return previous > alertThresholdPercent;
            }
            catch
            {
                return false;
            }
        }
    }

    private void EnsureSamplesLoaded(int sampleCount)
    {
        if (_loadedSampleCount >= sampleCount)
        {
            return;
        }

        _recentSamples.Clear();

        if (File.Exists(_logFilePath))
        {
            foreach (var line in File.ReadLines(_logFilePath).TakeLast(sampleCount))
            {
                var value = ParseMemoryValue(line);
                if (value.HasValue)
                {
                    _recentSamples.Enqueue(value.Value);
                }
            }
        }

        while (_recentSamples.Count > sampleCount)
        {
            _recentSamples.Dequeue();
        }

        _loadedSampleCount = sampleCount;
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
