namespace DeploymentGuardian.Models;

public class DeploymentReport
{
    public List<Alert> Alerts { get; set; } = new();
    public int RiskScore { get; set; }
}