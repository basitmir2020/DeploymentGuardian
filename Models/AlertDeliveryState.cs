namespace DeploymentGuardian.Models;

public class AlertDeliveryState
{
    public Dictionary<string, DateTimeOffset> LastSentUtcByAlertKey { get; set; } = new();
}
