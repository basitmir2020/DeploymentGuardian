namespace DeploymentGuardian.Models;

public class AppAnalysisResult
{
    public string AppType { get; set; } = "Unknown";
    public bool UsesDatabase { get; set; }
    public bool UsesBackgroundServices { get; set; }
    public bool UsesCaching { get; set; }
    public bool UsesFileUploads { get; set; }
    public int ExpectedConcurrentUsers { get; set; }
}