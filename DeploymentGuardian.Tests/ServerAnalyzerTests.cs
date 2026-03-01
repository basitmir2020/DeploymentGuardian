using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;
using DeploymentGuardian.Modules;

namespace DeploymentGuardian.Tests;

public class ServerAnalyzerTests
{
    [Fact]
    public void Analyze_ParsesMetricsFromShellResults()
    {
        var shell = new FakeShellHelper(new Dictionary<string, ShellCommandResult>
        {
            ["cat /proc/loadavg"] = Ok("0.50 0.20 0.10 1/100 1234"),
            ["nproc"] = Ok("4"),
            ["free -m"] = Ok("              total        used        free      shared  buff/cache   available\nMem:           1000         250         200          10         550         730"),
            ["df -h /"] = Ok("Filesystem      Size  Used Avail Use% Mounted on\n/dev/sda1        50G    5G   45G  10% /"),
            ["swapon --show"] = Ok("NAME      TYPE SIZE USED PRIO\n/swapfile file   2G   0B   -2")
        });

        var analyzer = new ServerAnalyzer(shell);
        var metrics = analyzer.Analyze();

        Assert.Equal(0.50, metrics.CpuLoad, 2);
        Assert.Equal(4, metrics.CpuCores);
        Assert.Equal(25.0, metrics.RamUsagePercent, 2);
        Assert.Equal(10.0, metrics.DiskUsagePercent, 2);
        Assert.True(metrics.SwapEnabled);
    }

    private static ShellCommandResult Ok(string stdOut)
    {
        return new ShellCommandResult
        {
            Command = "test",
            ExitCode = 0,
            StdOut = stdOut,
            StdErr = string.Empty
        };
    }

    private sealed class FakeShellHelper : IShellHelper
    {
        private readonly IReadOnlyDictionary<string, ShellCommandResult> _results;

        public FakeShellHelper(IReadOnlyDictionary<string, ShellCommandResult> results)
        {
            _results = results;
        }

        public ShellCommandResult RunCommand(string command)
        {
            if (_results.TryGetValue(command, out var result))
            {
                return result;
            }

            return new ShellCommandResult
            {
                Command = command,
                ExitCode = 1,
                StdErr = $"Missing fake result for command: {command}"
            };
        }

        public string Run(string command)
        {
            var result = RunCommand(command);
            return string.IsNullOrWhiteSpace(result.StdOut) ? result.StdErr : result.StdOut;
        }
    }
}
