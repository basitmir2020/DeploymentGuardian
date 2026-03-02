using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeploymentGuardian.Services;

public class TelegramSetupAssistant
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly string _token;
    private readonly string _chatId;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollInterval;
    private long _nextUpdateId;
    private bool _initialized;
    private PendingSetupPlan? _pendingPlan;

    /// <summary>
    /// Creates a Telegram setup assistant for stack-driven server provisioning requests.
    /// </summary>
    public TelegramSetupAssistant(string token, string chatId, ILogger logger, int pollIntervalSeconds = 5)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Telegram token is required.", nameof(token));
        }

        if (string.IsNullOrWhiteSpace(chatId))
        {
            throw new ArgumentException("Telegram chat id is required.", nameof(chatId));
        }

        if (pollIntervalSeconds < 1 || pollIntervalSeconds > 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollIntervalSeconds),
                "Telegram assistant poll interval must be between 1 and 60 seconds.");
        }

        _token = token.Trim();
        _chatId = chatId.Trim();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pollInterval = TimeSpan.FromSeconds(pollIntervalSeconds);
    }

    /// <summary>
    /// Runs continuous Telegram long-poll processing.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Telegram setup assistant started.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram setup assistant poll cycle failed.");
                await Task.Delay(_pollInterval, cancellationToken);
            }
        }

        _logger.LogInformation("Telegram setup assistant stopped.");
    }

    /// <summary>
    /// Polls Telegram updates and handles incoming chat commands.
    /// </summary>
    public async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        var updates = await GetUpdatesAsync(cancellationToken);
        if (updates.Count == 0)
        {
            return;
        }

        // On first poll, consume backlog without executing stale commands.
        if (!_initialized)
        {
            _nextUpdateId = updates.Max(u => u.UpdateId) + 1;
            _initialized = true;
            _logger.LogInformation("Telegram setup assistant initialized at update offset {Offset}.", _nextUpdateId);
            return;
        }

        foreach (var update in updates.OrderBy(u => u.UpdateId))
        {
            _nextUpdateId = Math.Max(_nextUpdateId, update.UpdateId + 1);

            if (!string.Equals(update.ChatId, _chatId, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(update.Text))
            {
                continue;
            }

            await HandleMessageAsync(update.Text.Trim(), cancellationToken);
        }
    }

    private async Task HandleMessageAsync(string text, CancellationToken cancellationToken)
    {
        if (IsHelpMessage(text))
        {
            await SendMessageAsync(BuildHelpMessage(), cancellationToken);
            return;
        }

        if (IsCancelMessage(text))
        {
            _pendingPlan = null;
            await SendMessageAsync("Pending setup request canceled.", cancellationToken);
            return;
        }

        if (string.Equals(text, "steps", StringComparison.OrdinalIgnoreCase))
        {
            if (_pendingPlan is null)
            {
                await SendMessageAsync("No pending setup plan. Tell me your app stack first.", cancellationToken);
                return;
            }

            await SendMessageAsync(BuildManualStepsMessage(_pendingPlan), cancellationToken);
            return;
        }

        if (IsOkayMessage(text))
        {
            await ExecutePendingPlanAsync(cancellationToken);
            return;
        }

        if (!TryResolveStack(text, out var definition))
        {
            await SendMessageAsync(
                "I can help with Angular, React, Next.js, Blazor, ASP.NET Core, Vue, and Node.js. " +
                "Example: \"My application is Next.js\".",
                cancellationToken);
            return;
        }

        var analysis = AnalyzeRequirements(definition);
        var missingResources = analysis.ResourceStatuses.Where(s => !s.IsInstalled).ToList();

        if (!missingResources.Any())
        {
            _pendingPlan = null;
            await SendMessageAsync(BuildReadinessMessage(definition, analysis.ResourceStatuses), cancellationToken);
            return;
        }

        _pendingPlan = new PendingSetupPlan(
            definition,
            analysis.ResourceStatuses,
            analysis.InstallActions,
            DateTimeOffset.UtcNow);

        await SendMessageAsync(BuildReadinessMessage(definition, analysis.ResourceStatuses), cancellationToken);
    }

    private async Task ExecutePendingPlanAsync(CancellationToken cancellationToken)
    {
        if (_pendingPlan is null)
        {
            await SendMessageAsync("No pending setup plan. Send your app stack first.", cancellationToken);
            return;
        }

        if (!OperatingSystem.IsLinux())
        {
            await SendMessageAsync(
                "Automated setup is currently supported on Linux hosts only.\n\n" +
                BuildManualStepsMessage(_pendingPlan),
                cancellationToken);
            return;
        }

        if (!_pendingPlan.InstallActions.Any())
        {
            await SendMessageAsync(
                "No executable automation steps were generated for the missing resources.\n\n" +
                BuildManualStepsMessage(_pendingPlan),
                cancellationToken);
            _pendingPlan = null;
            return;
        }

        var plan = _pendingPlan;
        await SendMessageAsync(
            $"Starting setup for {plan.Definition.DisplayName}. Total steps: {plan.InstallActions.Count}.",
            cancellationToken);

        var failures = new List<string>();

        for (var i = 0; i < plan.InstallActions.Count; i++)
        {
            var action = plan.InstallActions[i];
            var stepNumber = i + 1;
            await SendMessageAsync($"Step {stepNumber}/{plan.InstallActions.Count}: {action.Label}", cancellationToken);

            var result = await Task.Run(() => RunCommand(action.Command, 15 * 60 * 1000), cancellationToken);
            if (result.TimedOut)
            {
                failures.Add($"{action.Label}: command timed out.");
                await SendMessageAsync(
                    $"Step {stepNumber} timed out. Manual command:\n{action.Command}",
                    cancellationToken);
                continue;
            }

            if (result.ExitCode != 0)
            {
                var summary = FirstNonEmptyLine(result.StdErr, result.StdOut);
                failures.Add($"{action.Label}: exit code {result.ExitCode} ({summary})");
                await SendMessageAsync(
                    $"Step {stepNumber} failed (exit {result.ExitCode}).\n" +
                    $"Command: {action.Command}\n" +
                    $"Reason: {summary}",
                    cancellationToken);
                continue;
            }

            await SendMessageAsync($"Step {stepNumber} completed.", cancellationToken);
        }

        var finalAnalysis = AnalyzeRequirements(plan.Definition);
        var finalMessage = BuildReadinessMessage(plan.Definition, finalAnalysis.ResourceStatuses);
        if (failures.Any())
        {
            finalMessage += "\n\nCompleted with failures:\n" + string.Join("\n", failures.Select(f => "- " + f));
        }

        _pendingPlan = null;
        await SendMessageAsync(finalMessage, cancellationToken);
    }

    private static AnalysisResult AnalyzeRequirements(StackDefinition definition)
    {
        var resourceStatuses = new List<ResourceStatus>();

        foreach (var requirement in definition.Requirements)
        {
            var detectCommand = OperatingSystem.IsWindows()
                ? requirement.WindowsDetectCommand
                : requirement.LinuxDetectCommand;
            var versionCommand = OperatingSystem.IsWindows()
                ? requirement.WindowsVersionCommand
                : requirement.LinuxVersionCommand;

            if (string.IsNullOrWhiteSpace(detectCommand))
            {
                resourceStatuses.Add(new ResourceStatus(requirement, false, "unknown"));
                continue;
            }

            var probe = RunCommand(detectCommand, 8000);
            if (probe.ExitCode != 0 || probe.TimedOut)
            {
                resourceStatuses.Add(new ResourceStatus(requirement, false, "not installed"));
                continue;
            }

            var version = string.Empty;
            if (!string.IsNullOrWhiteSpace(versionCommand))
            {
                var versionResult = RunCommand(versionCommand, 8000);
                if (!versionResult.TimedOut)
                {
                    version = FirstNonEmptyLine(versionResult.StdOut, versionResult.StdErr);
                }
            }

            resourceStatuses.Add(new ResourceStatus(
                requirement,
                true,
                string.IsNullOrWhiteSpace(version) ? "installed" : version));
        }

        var installActions = resourceStatuses
            .Where(s => !s.IsInstalled)
            .Select(s => s.Requirement)
            .Select(r => OperatingSystem.IsWindows()
                ? new InstallAction(r.InstallLabel, r.WindowsInstallCommand)
                : new InstallAction(r.InstallLabel, r.LinuxInstallCommand))
            .Where(a => !string.IsNullOrWhiteSpace(a.Command))
            .GroupBy(a => a.Command, StringComparer.Ordinal)
            .Select(g => new InstallAction(string.Join(" + ", g.Select(x => x.Label).Distinct()), g.Key))
            .ToList();

        return new AnalysisResult(resourceStatuses, installActions);
    }

    private string BuildReadinessMessage(StackDefinition definition, IReadOnlyList<ResourceStatus> statuses)
    {
        var lines = new List<string>
        {
            $"Server readiness check for {definition.DisplayName}",
            $"Platform: {(OperatingSystem.IsWindows() ? "Windows" : "Linux")}",
            string.Empty
        };

        foreach (var status in statuses)
        {
            var type = status.Requirement.Required ? "required" : "recommended";
            var availability = status.IsInstalled ? "OK" : "Missing";
            var detail = status.IsInstalled && !string.IsNullOrWhiteSpace(status.Version)
                ? $" ({status.Version})"
                : string.Empty;
            lines.Add($"- {status.Requirement.Name} [{type}]: {availability}{detail}");
        }

        var missingRequired = statuses.Where(s => !s.IsInstalled && s.Requirement.Required).ToList();
        var missingAny = statuses.Where(s => !s.IsInstalled).ToList();

        lines.Add(string.Empty);

        if (!missingAny.Any())
        {
            lines.Add($"Server is ready for {definition.DisplayName}. No setup actions needed.");
            return string.Join("\n", lines);
        }

        if (!missingRequired.Any())
        {
            lines.Add($"Required resources are present for {definition.DisplayName}.");
            lines.Add("Only recommended resources are missing.");
        }
        else
        {
            lines.Add($"Missing required resources: {string.Join(", ", missingRequired.Select(s => s.Requirement.Name))}");
        }

        if (OperatingSystem.IsLinux())
        {
            lines.Add("Reply `okay` to run setup commands now.");
        }
        else
        {
            lines.Add("Automated setup is available on Linux only in this version.");
        }

        lines.Add("Reply `steps` to view commands, or `cancel` to discard this plan.");
        return string.Join("\n", lines);
    }

    private static string BuildManualStepsMessage(PendingSetupPlan plan)
    {
        var lines = new List<string>
        {
            $"Setup commands for {plan.Definition.DisplayName}:"
        };

        if (!plan.InstallActions.Any())
        {
            lines.Add("- No predefined commands available for this platform.");
            return string.Join("\n", lines);
        }

        foreach (var action in plan.InstallActions)
        {
            lines.Add($"- {action.Label}: {action.Command}");
        }

        return string.Join("\n", lines);
    }

    private static bool IsHelpMessage(string text)
    {
        return string.Equals(text, "/start", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "/help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "help", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOkayMessage(string text)
    {
        var normalized = text.Trim().TrimEnd('.', '!', '?').ToLowerInvariant();
        return normalized is "ok" or "okay" or "yes" or "start" or "go ahead";
    }

    private static bool IsCancelMessage(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        return normalized is "cancel" or "stop" or "abort";
    }

    private static string BuildHelpMessage()
    {
        return
            "Send your app stack and I will check server readiness.\n" +
            "Examples:\n" +
            "- My application is Angular\n" +
            "- My app is Next.js\n" +
            "- We are using Blazor\n\n" +
            "Then reply `okay` to start setup, `steps` to view commands, or `cancel`.";
    }

    private static bool TryResolveStack(string text, out StackDefinition definition)
    {
        var normalized = text.ToLowerInvariant();

        if (normalized.Contains("next.js") || normalized.Contains("nextjs") || normalized.Contains("next js"))
        {
            definition = BuildNodeWebDefinition("Next.js");
            return true;
        }

        if (normalized.Contains("angular"))
        {
            definition = BuildNodeWebDefinition("Angular");
            return true;
        }

        if (normalized.Contains("react"))
        {
            definition = BuildNodeWebDefinition("React");
            return true;
        }

        if (normalized.Contains("vue"))
        {
            definition = BuildNodeWebDefinition("Vue.js");
            return true;
        }

        if (normalized.Contains("blazor"))
        {
            definition = BuildBlazorDefinition();
            return true;
        }

        if (normalized.Contains("asp.net") || normalized.Contains("aspnet") || normalized.Contains(".net") || normalized.Contains("dotnet"))
        {
            definition = BuildBlazorDefinition("ASP.NET Core");
            return true;
        }

        if (normalized.Contains("node"))
        {
            definition = BuildNodeWebDefinition("Node.js");
            return true;
        }

        definition = null!;
        return false;
    }

    private static StackDefinition BuildNodeWebDefinition(string displayName)
    {
        return new StackDefinition(
            displayName,
            new[]
            {
                new ResourceRequirement(
                    "Node.js (LTS)",
                    true,
                    "command -v node >/dev/null 2>&1",
                    "node --version",
                    "Install Node.js and npm",
                    "apt-get update && apt-get install -y nodejs npm",
                    "where.exe node > $null 2>&1; if ($LASTEXITCODE -eq 0) { exit 0 } else { exit 1 }",
                    "node --version",
                    "winget install OpenJS.NodeJS.LTS --accept-package-agreements --accept-source-agreements"),
                new ResourceRequirement(
                    "npm",
                    true,
                    "command -v npm >/dev/null 2>&1",
                    "npm --version",
                    "Install Node.js and npm",
                    "apt-get update && apt-get install -y nodejs npm",
                    "where.exe npm > $null 2>&1; if ($LASTEXITCODE -eq 0) { exit 0 } else { exit 1 }",
                    "npm --version",
                    "winget install OpenJS.NodeJS.LTS --accept-package-agreements --accept-source-agreements"),
                new ResourceRequirement(
                    "PM2",
                    false,
                    "command -v pm2 >/dev/null 2>&1",
                    "pm2 --version",
                    "Install PM2 process manager",
                    "npm install -g pm2",
                    "where.exe pm2 > $null 2>&1; if ($LASTEXITCODE -eq 0) { exit 0 } else { exit 1 }",
                    "pm2 --version",
                    "npm install -g pm2"),
                new ResourceRequirement(
                    "Nginx",
                    false,
                    "command -v nginx >/dev/null 2>&1",
                    "nginx -v 2>&1",
                    "Install and start Nginx",
                    "apt-get install -y nginx && systemctl enable --now nginx",
                    "where.exe nginx > $null 2>&1; if ($LASTEXITCODE -eq 0) { exit 0 } else { exit 1 }",
                    "nginx -v",
                    string.Empty)
            });
    }

    private static StackDefinition BuildBlazorDefinition(string displayName = "Blazor")
    {
        return new StackDefinition(
            displayName,
            new[]
            {
                new ResourceRequirement(
                    ".NET SDK",
                    true,
                    "command -v dotnet >/dev/null 2>&1",
                    "dotnet --version",
                    "Install .NET SDK 8.0",
                    "apt-get update && apt-get install -y dotnet-sdk-8.0",
                    "where.exe dotnet > $null 2>&1; if ($LASTEXITCODE -eq 0) { exit 0 } else { exit 1 }",
                    "dotnet --version",
                    "winget install Microsoft.DotNet.SDK.8 --accept-package-agreements --accept-source-agreements"),
                new ResourceRequirement(
                    "ASP.NET Core Runtime",
                    true,
                    "dotnet --list-runtimes | grep -q Microsoft.AspNetCore.App",
                    "dotnet --list-runtimes | grep Microsoft.AspNetCore.App | head -n 1",
                    "Install ASP.NET Core Runtime 8.0",
                    "apt-get install -y aspnetcore-runtime-8.0",
                    "$r = dotnet --list-runtimes 2>$null | Select-String 'Microsoft.AspNetCore.App'; if ($r) { exit 0 } else { exit 1 }",
                    "dotnet --list-runtimes | Select-String 'Microsoft.AspNetCore.App' | Select-Object -First 1",
                    "winget install Microsoft.DotNet.AspNetCore.8 --accept-package-agreements --accept-source-agreements"),
                new ResourceRequirement(
                    "Nginx reverse proxy",
                    false,
                    "command -v nginx >/dev/null 2>&1",
                    "nginx -v 2>&1",
                    "Install and start Nginx",
                    "apt-get install -y nginx && systemctl enable --now nginx",
                    "where.exe nginx > $null 2>&1; if ($LASTEXITCODE -eq 0) { exit 0 } else { exit 1 }",
                    "nginx -v",
                    string.Empty)
            });
    }

    private async Task<IReadOnlyList<TelegramInboundMessage>> GetUpdatesAsync(CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Clamp((int)_pollInterval.TotalSeconds, 1, 30);
        var endpoint =
            $"https://api.telegram.org/bot{_token}/getUpdates?offset={_nextUpdateId}&limit=20&timeout={timeoutSeconds}&allowed_updates=%5B%22message%22%5D";

        using var response = await HttpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<TelegramInboundMessage>();
        }

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
        {
            return Array.Empty<TelegramInboundMessage>();
        }

        if (!document.RootElement.TryGetProperty("result", out var resultElement) ||
            resultElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TelegramInboundMessage>();
        }

        var updates = new List<TelegramInboundMessage>();
        foreach (var updateElement in resultElement.EnumerateArray())
        {
            if (!updateElement.TryGetProperty("update_id", out var updateIdElement))
            {
                continue;
            }

            if (!updateIdElement.TryGetInt64(out var updateId))
            {
                continue;
            }

            if (!updateElement.TryGetProperty("message", out var messageElement))
            {
                continue;
            }

            if (!messageElement.TryGetProperty("chat", out var chatElement) ||
                !chatElement.TryGetProperty("id", out var chatIdElement))
            {
                continue;
            }

            var chatId = chatIdElement.ValueKind switch
            {
                JsonValueKind.Number => chatIdElement.GetInt64().ToString(CultureInfo.InvariantCulture),
                JsonValueKind.String => chatIdElement.GetString() ?? string.Empty,
                _ => string.Empty
            };

            if (!messageElement.TryGetProperty("text", out var textElement))
            {
                continue;
            }

            var text = textElement.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            updates.Add(new TelegramInboundMessage(updateId, chatId, text));
        }

        return updates;
    }

    private async Task SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var safeMessage = message.Length > 3900
            ? message[..3875] + "\n\n[Message truncated]"
            : message;

        using var response = await HttpClient.PostAsync(
            $"https://api.telegram.org/bot{_token}/sendMessage",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("chat_id", _chatId),
                new KeyValuePair<string, string>("text", safeMessage)
            ]),
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static CommandResult RunCommand(string command, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new CommandResult(-1, string.Empty, "Command is empty.", false);
        }

        using var process = new Process();
        process.StartInfo = CreateProcessStartInfo(command);
        process.Start();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill failures when process already exited.
            }

            return new CommandResult(-1, stdout.Trim(), stderr.Trim(), true);
        }

        return new CommandResult(process.ExitCode, stdout.Trim(), stderr.Trim(), false);
    }

    private static ProcessStartInfo CreateProcessStartInfo(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "powershell";
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-EncodedCommand");
            startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
            return startInfo;
        }

        startInfo.FileName = "/bin/bash";
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    private static string FirstNonEmptyLine(params string[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var line = value
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return "No additional output.";
    }

    private sealed record TelegramInboundMessage(long UpdateId, string ChatId, string Text);
    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);
    private sealed record InstallAction(string Label, string Command);
    private sealed record AnalysisResult(IReadOnlyList<ResourceStatus> ResourceStatuses, IReadOnlyList<InstallAction> InstallActions);
    private sealed record PendingSetupPlan(
        StackDefinition Definition,
        IReadOnlyList<ResourceStatus> ResourceStatuses,
        IReadOnlyList<InstallAction> InstallActions,
        DateTimeOffset RequestedAtUtc);
    private sealed record StackDefinition(string DisplayName, IReadOnlyList<ResourceRequirement> Requirements);
    private sealed record ResourceStatus(ResourceRequirement Requirement, bool IsInstalled, string Version);
    private sealed record ResourceRequirement(
        string Name,
        bool Required,
        string LinuxDetectCommand,
        string LinuxVersionCommand,
        string InstallLabel,
        string LinuxInstallCommand,
        string WindowsDetectCommand,
        string WindowsVersionCommand,
        string WindowsInstallCommand);
}
