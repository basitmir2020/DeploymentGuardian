namespace DeploymentGuardian.Models;

public class SecurityReport
{
    public bool FirewallEnabled { get; set; }
    public bool RootLoginDisabled { get; set; }
    public bool PasswordAuthDisabled { get; set; }
    public bool Fail2BanInstalled { get; set; }
    public List<int> OpenPorts { get; set; } = new();
}