using System.Diagnostics;
using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class WindowsServiceCollector : IServiceDataCollector
{
    /// <summary>
    /// Collects running Windows service names via sc.exe output.
    /// </summary>
    public ServiceReport Scan()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc",
                Arguments = "query type= service state= all",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return new ServiceReport();
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return new ServiceReport
            {
                RunningServices = ParseRunningServices(output)
            };
        }
        catch
        {
            return new ServiceReport();
        }
    }

    /// <summary>
    /// Parses sc.exe service query output and extracts running service names.
    /// </summary>
    private static List<string> ParseRunningServices(string output)
    {
        var services = new List<string>();
        var currentService = string.Empty;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
            {
                currentService = trimmed["SERVICE_NAME:".Length..].Trim();
                continue;
            }

            if (trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(currentService))
            {
                services.Add(currentService);
            }
        }

        return services.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
