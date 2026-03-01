namespace DeploymentGuardian.Models;

public static class AlertCodes
{
    public const string Generic = "GENERIC";
    public const string CpuSpike = "CPU_SPIKE";
    public const string DiskUsageHigh = "DISK_USAGE_HIGH";
    public const string RamUsageHigh = "RAM_USAGE_HIGH";
    public const string MemoryLeakTrend = "MEMORY_LEAK_TREND";
    public const string FirewallDisabled = "FIREWALL_DISABLED";
    public const string RootLoginEnabled = "ROOT_LOGIN_ENABLED";
    public const string SshPasswordAuthEnabled = "SSH_PASSWORD_AUTH_ENABLED";
    public const string Fail2BanInactive = "FAIL2BAN_INACTIVE";
    public const string OpenPortsHigh = "OPEN_PORTS_HIGH";
    public const string ProcessDataMissing = "PROCESS_DATA_MISSING";
    public const string ProcessCpuHigh = "PROCESS_CPU_HIGH";
    public const string ProcessMemoryHigh = "PROCESS_MEMORY_HIGH";
    public const string ServicesNoneRunning = "SERVICES_NONE_RUNNING";
    public const string SshServiceMissing = "SSH_SERVICE_MISSING";
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public class Alert
{
    public string Code { get; set; } = AlertCodes.Generic;
    public string? Target { get; set; }
    public required string Message { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Info;
}
