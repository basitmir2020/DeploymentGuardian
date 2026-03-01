using System.Diagnostics;
using System.Globalization;

namespace DeploymentGuardian.Modules;

internal static class WindowsCollectorUtils
{
    /// <summary>
    /// Attempts to read total/free physical memory bytes from Win32_OperatingSystem.
    /// </summary>
    public static bool TryGetPhysicalMemoryBytes(out double totalBytes, out double freeBytes)
    {
        totalBytes = 0;
        freeBytes = 0;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"$os=Get-CimInstance Win32_OperatingSystem; Write-Output \\\"$($os.TotalVisibleMemorySize)|$($os.FreePhysicalMemory)\\\"\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            var parts = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                return false;
            }

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var totalKb) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var freeKb))
            {
                return false;
            }

            totalBytes = totalKb * 1024.0;
            freeBytes = freeKb * 1024.0;
            return totalBytes > 0;
        }
        catch
        {
            return false;
        }
    }
}
