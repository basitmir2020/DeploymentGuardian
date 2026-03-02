namespace DeploymentGuardian.Models;

public class ServerMetrics
{
    public double CpuLoad { get; set; }
    public int CpuCores { get; set; }
    public double RamTotalMb { get; set; }
    public double RamUsagePercent { get; set; }
    public double DiskUsagePercent { get; set; }
    public bool SwapEnabled { get; set; }
}