using System.Text.Json;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class AlertHistoryStore
{
    private readonly string _historyFilePath;
    private readonly int _maxEntries;
    private readonly object _lock = new();

    /// <summary>
    /// Creates an alert history store backed by a JSONL file with max entry retention.
    /// </summary>
    public AlertHistoryStore(string historyFilePath, int maxEntries)
    {
        _historyFilePath = historyFilePath;
        _maxEntries = Math.Max(1, maxEntries);
    }

    /// <summary>
    /// Appends a cycle entry to history and applies retention trimming.
    /// </summary>
    public void Append(AlertHistoryEntry entry)
    {
        lock (_lock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_historyFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var line = JsonSerializer.Serialize(entry);
                File.AppendAllText(_historyFilePath, line + Environment.NewLine);

                TrimIfNeeded();
            }
            catch
            {
                // History write failures must not break runtime alerting.
            }
        }
    }

    /// <summary>
    /// Trims oldest history entries when file exceeds configured retention size.
    /// </summary>
    private void TrimIfNeeded()
    {
        if (!File.Exists(_historyFilePath))
        {
            return;
        }

        var lines = File.ReadAllLines(_historyFilePath);
        if (lines.Length <= _maxEntries)
        {
            return;
        }

        var trimmed = lines.TakeLast(_maxEntries).ToArray();
        File.WriteAllLines(_historyFilePath, trimmed);
    }
}
