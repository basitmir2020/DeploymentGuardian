using DeploymentGuardian.Models;

namespace DeploymentGuardian.Abstractions;

public interface IShellHelper
{
    /// <summary>
    /// Executes a shell command and returns structured execution output.
    /// </summary>
    ShellCommandResult RunCommand(string command);

    /// <summary>
    /// Executes a shell command and returns textual output.
    /// </summary>
    string Run(string command);
}
