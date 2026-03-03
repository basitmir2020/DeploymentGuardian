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
        process.StartInfo.ArgumentList.Add("-lc");
        process.StartInfo.ArgumentList.Add(command);
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

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

            var output = outputTask.IsCompleted ? outputTask.GetAwaiter().GetResult() : string.Empty;
            var error = errorTask.IsCompleted ? errorTask.GetAwaiter().GetResult() : string.Empty;

            return new ShellCommandResult
            {
                Command = command,
                StdOut = output.Trim(),
                StdErr = error.Trim(),
                ExitCode = -1,
                TimedOut = true
            };
        }

        var finalOutput = outputTask.GetAwaiter().GetResult();
        var finalError = errorTask.GetAwaiter().GetResult();

        return new ShellCommandResult
        {
            Command = command,
            StdOut = finalOutput.Trim(),
            StdErr = finalError.Trim(),
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
