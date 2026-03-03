using System.Text.Json;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class AlertHistoryStore
{
    private readonly string _historyFilePath;
    private readonly int _maxEntries;
    private readonly object _lock = new();
    private readonly Queue<string> _recentLines;
    private int _lineCount;
    private readonly int _compactionThreshold;

    /// <summary>
    /// Creates an alert history store backed by a JSONL file with max entry retention.
    /// </summary>
    public AlertHistoryStore(string historyFilePath, int maxEntries)
    {
        _historyFilePath = historyFilePath;
        _maxEntries = Math.Max(1, maxEntries);
        _compactionThreshold = Math.Max(1, Math.Min(500, _maxEntries / 10));
        _recentLines = new Queue<string>(_maxEntries);
        _lineCount = InitializeStateFromFile();
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
                if (_lineCount < _maxEntries)
                {
                    File.AppendAllText(_historyFilePath, line + Environment.NewLine);
                    _recentLines.Enqueue(line);
                    _lineCount++;
                    return;
                }

                File.AppendAllText(_historyFilePath, line + Environment.NewLine);
                _lineCount++;
                _recentLines.Enqueue(line);
                while (_recentLines.Count > _maxEntries)
                {
                    _recentLines.Dequeue();
                }

                if (_lineCount >= _maxEntries + _compactionThreshold)
                {
                    CompactHistoryFile();
                }
            }
            catch
            {
                // History write failures must not break runtime alerting.
            }
        }
    }

    /// <summary>
    /// Loads current history line count and tail cache from disk.
    /// </summary>
    private int InitializeStateFromFile()
    {
        if (!File.Exists(_historyFilePath))
        {
            return 0;
        }

        try
        {
            var lineCount = 0;
            foreach (var line in File.ReadLines(_historyFilePath))
            {
                lineCount++;
                _recentLines.Enqueue(line);
                if (_recentLines.Count > _maxEntries)
                {
                    _recentLines.Dequeue();
                }
            }

            if (lineCount > _maxEntries)
            {
                // Normalize oversized history once on startup.
                CompactHistoryFile();
                return _lineCount;
            }

            return lineCount;
        }
        catch
        {
            _recentLines.Clear();
            return 0;
        }
    }

    private void CompactHistoryFile()
    {
        var directory = Path.GetDirectoryName(_historyFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllLines(_historyFilePath, _recentLines);
        _lineCount = _recentLines.Count;
    }
}
