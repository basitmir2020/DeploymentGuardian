using System.Diagnostics;
using System.Net.NetworkInformation;
using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class WindowsSecurityCollector : ISecurityDataCollector
{
    private static readonly TimeSpan FirewallCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly object FirewallCacheLock = new();
    private static DateTimeOffset _firewallCachedAtUtc = DateTimeOffset.MinValue;
    private static bool _cachedFirewallEnabled;

    /// <summary>
    /// Collects Windows security posture data where equivalent checks are available.
    /// </summary>
    public SecurityReport Analyze()
    {
        return new SecurityReport
        {
            FirewallEnabled = IsFirewallEnabled(),
            RootLoginDisabled = true,
            PasswordAuthDisabled = true,
            Fail2BanInstalled = true,
            OpenPorts = GetListeningTcpPorts()
        };
    }

    /// <summary>
    /// Checks whether any Windows firewall profile is ON.
    /// </summary>
    private static bool IsFirewallEnabled()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _firewallCachedAtUtc < FirewallCacheTtl)
        {
            return _cachedFirewallEnabled;
        }

        lock (FirewallCacheLock)
        {
            now = DateTimeOffset.UtcNow;
            if (now - _firewallCachedAtUtc < FirewallCacheTtl)
            {
                return _cachedFirewallEnabled;
            }

            _cachedFirewallEnabled = ReadFirewallEnabled();
            _firewallCachedAtUtc = now;
            return _cachedFirewallEnabled;
        }
    }

    private static bool ReadFirewallEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "advfirewall show allprofiles",
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

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return output.Contains("State", StringComparison.OrdinalIgnoreCase) &&
                   output.Contains("ON", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns distinct listening TCP ports from local network properties.
    /// </summary>
    private static List<int> GetListeningTcpPorts()
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Select(endpoint => endpoint.Port)
                .Distinct()
                .OrderBy(port => port)
                .ToList();
        }
        catch
        {
            return new List<int>();
        }
    }
}
