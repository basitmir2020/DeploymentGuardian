namespace DeploymentGuardian.Models;

public class GuardianOptions
{
    public string MemoryLogFilePath { get; set; } = "/tmp/guardian-memory.log";
    public string AlertStateFilePath { get; set; } = "/tmp/guardian-alert-state.json";
    public string AlertHistoryFilePath { get; set; } = "/tmp/guardian-history.jsonl";
    public int AlertHistoryMaxEntries { get; set; } = 5000;
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookAuthHeader { get; set; } = "Authorization";
    public bool EnableOllamaSuggestions { get; set; }
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.2";
    public bool EnableLlamaCppSuggestions { get; set; }
    public string LlamaCppBaseUrl { get; set; } = "http://localhost:8080";
    public string LlamaCppModel { get; set; } = "local-model";
    public double CpuSpikeMultiplier { get; set; } = 1.5;
    public double DiskUsageWarningPercent { get; set; } = 85;
    public double RamUsageWarningPercent { get; set; } = 85;
    public double ProcessCpuCriticalPercent { get; set; } = 85;
    public double ProcessMemoryWarningPercent { get; set; } = 30;
    public int MaxOpenPortsWarningCount { get; set; } = 20;
    public double MemoryTrendAlertPercent { get; set; } = 70;
    public int MemoryTrendSamples { get; set; } = 5;
    public double RiskRamPercent { get; set; } = 80;
    public double AlertCooldownMinutes { get; set; } = 30;
    public int NotificationMaxAttempts { get; set; } = 3;
    public int NotificationBaseDelaySeconds { get; set; } = 2;
    public int ScanIntervalSeconds { get; set; }
    public bool EnableOpenAiSuggestions { get; set; }
}
