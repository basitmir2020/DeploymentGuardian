namespace DeploymentGuardian.Models;

public class ShellCommandResult
{
    public required string Command { get; set; }
    public string StdOut { get; set; } = string.Empty;
    public string StdErr { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public bool TimedOut { get; set; }
    public bool Succeeded => !TimedOut && ExitCode == 0;
}
