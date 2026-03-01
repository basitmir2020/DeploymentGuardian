using System.Text.Json;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class AlertDeduplicator
{
    private readonly string _stateFilePath;
    private readonly TimeSpan _cooldown;
    private readonly object _stateLock = new();
    private AlertDeliveryState _state;

    /// <summary>
    /// Creates an alert deduplicator with persisted state and cooldown window.
    /// </summary>
    public AlertDeduplicator(string stateFilePath, TimeSpan cooldown)
    {
        _stateFilePath = stateFilePath;
        _cooldown = cooldown < TimeSpan.Zero ? TimeSpan.Zero : cooldown;
        _state = LoadState();
    }

    /// <summary>
    /// Returns alerts that are eligible to be sent now based on cooldown history.
    /// </summary>
    public List<Alert> FilterSendable(IEnumerable<Alert> alerts, DateTimeOffset nowUtc)
    {
        lock (_stateLock)
        {
            return alerts
                .Where(alert => ShouldSend(alert, nowUtc))
                .ToList();
        }
    }

    /// <summary>
    /// Marks alerts as sent at the provided time and persists delivery state.
    /// </summary>
    public void MarkSent(IEnumerable<Alert> alerts, DateTimeOffset nowUtc)
    {
        lock (_stateLock)
        {
            foreach (var alert in alerts)
            {
                _state.LastSentUtcByAlertKey[BuildKey(alert)] = nowUtc;
            }

            SaveState();
        }
    }

    /// <summary>
    /// Returns true when the alert has not been sent within the cooldown period.
    /// </summary>
    private bool ShouldSend(Alert alert, DateTimeOffset nowUtc)
    {
        var key = BuildKey(alert);
        if (!_state.LastSentUtcByAlertKey.TryGetValue(key, out var lastSent))
        {
            return true;
        }

        return (nowUtc - lastSent) >= _cooldown;
    }

    /// <summary>
    /// Builds a stable dedup key from alert severity and normalized message.
    /// </summary>
    private static string BuildKey(Alert alert)
    {
        var code = string.IsNullOrWhiteSpace(alert.Code)
            ? AlertCodes.Generic
            : alert.Code.Trim().ToUpperInvariant();
        var target = alert.Target?.Trim().ToLowerInvariant() ?? string.Empty;
        return $"{code}:{target}:{alert.Severity}";
    }

    /// <summary>
    /// Loads delivery state from disk, falling back to a new state on errors.
    /// </summary>
    private AlertDeliveryState LoadState()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return new AlertDeliveryState();
            }

            var json = File.ReadAllText(_stateFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AlertDeliveryState();
            }

            var state = JsonSerializer.Deserialize<AlertDeliveryState>(json);
            return state ?? new AlertDeliveryState();
        }
        catch
        {
            return new AlertDeliveryState();
        }
    }

    /// <summary>
    /// Persists current delivery state to disk.
    /// </summary>
    private void SaveState()
    {
        try
        {
            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch
        {
            // Swallow state persistence failures; runtime alerting should continue.
        }
    }
}
