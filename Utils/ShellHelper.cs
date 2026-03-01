using System.Diagnostics;
using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Utils;

public class ShellHelper : IShellHelper
{
    private const int DefaultTimeoutMs = 15000;

    /// <summary>
    /// Executes a shell command and returns structured execution output.
    /// </summary>
    public ShellCommandResult RunCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ShellCommandResult
            {
                Command = string.Empty,
                ExitCode = -1,
                StdErr = "Command is empty."
            };
        }

        using var process = new Process();
        process.StartInfo.FileName = "/bin/bash";
        process.StartInfo.Arguments = $"-c \"{command}\"";
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(DefaultTimeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill failures after timeout.
            }

            return new ShellCommandResult
            {
                Command = command,
                StdOut = output.Trim(),
                StdErr = error.Trim(),
                ExitCode = -1,
                TimedOut = true
            };
        }

        return new ShellCommandResult
        {
            Command = command,
            StdOut = output.Trim(),
            StdErr = error.Trim(),
            ExitCode = process.ExitCode
        };
    }

    /// <summary>
    /// Executes a shell command and returns textual output.
    /// </summary>
    public string Run(string command)
    {
        var result = RunCommand(command);
        return string.IsNullOrWhiteSpace(result.StdOut)
            ? result.StdErr
            : result.StdOut;
    }
}
