namespace DeploymentGuardian.Models;

public class AlertHistoryEntry
{
    public DateTimeOffset TimestampUtc { get; set; }
    public int RiskScore { get; set; }
    public int TotalAlerts { get; set; }
    public int SentAlerts { get; set; }
    public int SuppressedAlerts { get; set; }
    public bool DeliverySucceeded { get; set; }
    public string? DeliveryError { get; set; }
    public double CpuLoad { get; set; }
    public int CpuCores { get; set; }
    public double RamUsagePercent { get; set; }
    public double DiskUsagePercent { get; set; }
    public int OpenPortsCount { get; set; }
    public int RunningServicesCount { get; set; }
    public int ProcessesTracked { get; set; }
    public List<string> AlertCodes { get; set; } = new();
}
