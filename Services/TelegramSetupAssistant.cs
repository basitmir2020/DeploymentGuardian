using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DeploymentGuardian.Abstractions;
using Microsoft.Extensions.Logging;

namespace DeploymentGuardian.Services;

public class TelegramSetupAssistant
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions PlannerJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _token;
    private readonly string _chatId;
    private readonly IAiAdvisor? _aiAdvisor;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollInterval;
    private readonly bool _useSudoForInstallCommands;
    private readonly bool _stopOnRequiredFailure;
    private long _nextUpdateId;
    private bool _initialized;
    private PendingSetupPlan? _pendingPlan;

    public TelegramSetupAssistant(
        string token,
        string chatId,
        ILogger logger,
        int pollIntervalSeconds = 5,
        bool useSudoForInstallCommands = false,
        bool stopOnRequiredFailure = true,
        IAiAdvisor? aiAdvisor = null)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Telegram token is required.", nameof(token));
        }

        if (string.IsNullOrWhiteSpace(chatId))
        {
            throw new ArgumentException("Telegram chat id is required.", nameof(chatId));
        }

        if (pollIntervalSeconds is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollIntervalSeconds),
                "Telegram assistant poll interval must be between 1 and 60 seconds.");
        }

        _token = token.Trim();
        _chatId = chatId.Trim();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pollInterval = TimeSpan.FromSeconds(pollIntervalSeconds);
        _useSudoForInstallCommands = useSudoForInstallCommands;
        _stopOnRequiredFailure = stopOnRequiredFailure;
        _aiAdvisor = aiAdvisor;
    }

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

    public async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        var updates = await GetUpdatesAsync(cancellationToken);
        
        if (!_initialized)
        {
            _initialized = true;
            
            // Skip any pre-existing backlog only once on startup.
            if (updates.Count > 0)
            {
                _nextUpdateId = updates.Max(u => u.UpdateId) + 1;
                _logger.LogInformation("Telegram setup assistant initialized at update offset {Offset}.", _nextUpdateId);
                return;
            }
        }

        if (updates.Count == 0)
        {
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
            await SendMessageAsync("Pending AI setup plan canceled.", cancellationToken);
            return;
        }

        if (string.Equals(text, "steps", StringComparison.OrdinalIgnoreCase))
        {
            if (_pendingPlan is null)
            {
                await SendMessageAsync("No pending setup plan. Send your app technology first.", cancellationToken);
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

        var statusMessageId = await SendMessageAsync("Analyzing server and generating AI setup plan. Please wait...", cancellationToken);
        var (plan, error) = await BuildPlanFromAiAsync(text, statusMessageId, cancellationToken);
        if (plan is null)
        {
            await SendMessageAsync($"Could not generate setup plan.\nReason: {error}", cancellationToken);
            return;
        }

        _pendingPlan = plan;
        await SendMessageAsync(BuildReadinessMessage(plan), cancellationToken);
    }

    private async Task<(PendingSetupPlan? Plan, string Error)> BuildPlanFromAiAsync(
        string userRequest,
        int? statusMessageId,
        CancellationToken cancellationToken)
    {
        if (_aiAdvisor is null)
        {
            return (null, "AI advisor is not configured.");
        }

        var snapshot = await Task.Run(BuildServerSnapshotForPrompt, cancellationToken);
        var prompt = BuildPlannerPrompt(userRequest, snapshot);

        var sb = new StringBuilder();
        var lastUpdate = DateTime.UtcNow;

        try
        {
            await foreach (var chunk in _aiAdvisor.GetSuggestionsStreamAsync(prompt, cancellationToken))
            {
                sb.Append(chunk);
                
                // Update status message every 1.5 seconds to show progress
                if (statusMessageId.HasValue && (DateTime.UtcNow - lastUpdate).TotalSeconds > 1.5)
                {
                    var preview = sb.ToString();
                    // Truncate if too long for preview
                    if (preview.Length > 3500) preview = preview[..3500] + "...";
                    
                    await EditMessageAsync(statusMessageId.Value, $"Generating plan...\n\n```json\n{preview}\n```", cancellationToken);
                    lastUpdate = DateTime.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            return (null, $"AI request failed ({ex.Message}).");
        }

        var aiResponse = sb.ToString();

        if (!TryParseAiPlan(aiResponse, userRequest, out var parsed, out var parseError))
        {
            return (null, $"AI returned an unstructured plan ({parseError}).");
        }

        return (new PendingSetupPlan(
            parsed.Technology,
            parsed.Summary,
            parsed.Prerequisites,
            parsed.InstallActions,
            parsed.Notes,
            DateTimeOffset.UtcNow), string.Empty);
    }

    private async Task ExecutePendingPlanAsync(CancellationToken cancellationToken)
    {
        if (_pendingPlan is null)
        {
            await SendMessageAsync("No pending setup plan. Send your app technology first.", cancellationToken);
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
                "AI plan has no executable setup commands.\n\n" +
                BuildManualStepsMessage(_pendingPlan),
                cancellationToken);
            _pendingPlan = null;
            return;
        }

        var plan = _pendingPlan;
        var failures = new List<string>();
        await SendMessageAsync($"Starting AI setup for {plan.Technology}. Total steps: {plan.InstallActions.Count}.", cancellationToken);

        for (var i = 0; i < plan.InstallActions.Count; i++)
        {
            var action = plan.InstallActions[i];
            var stepNo = i + 1;
            await SendMessageAsync($"Step {stepNo}/{plan.InstallActions.Count}: {action.Label}", cancellationToken);

            var cmd = BuildExecutionCommand(action.Command);
            var result = await Task.Run(() => RunCommand(cmd, 15 * 60 * 1000), cancellationToken);

            if (result.TimedOut)
            {
                failures.Add($"{action.Label}: command timed out.");
                await SendMessageAsync($"Step {stepNo} timed out. Manual command:\n{action.Command}", cancellationToken);
                if (_stopOnRequiredFailure && action.IsRequired)
                {
                    await SendMessageAsync($"Halting setup because required step timed out: {action.Label}.", cancellationToken);
                    break;
                }

                continue;
            }

            if (result.ExitCode != 0)
            {
                var summary = FirstNonEmptyLine(result.StdErr, result.StdOut);
                var hint = ShouldSuggestSudo(summary)
                    ? "\nHint: set TELEGRAM_SETUP_ASSISTANT_USE_SUDO=true and allow sudoers commands."
                    : string.Empty;
                failures.Add($"{action.Label}: exit code {result.ExitCode} ({summary})");

                await SendMessageAsync(
                    $"Step {stepNo} failed (exit {result.ExitCode}).\nCommand: {action.Command}\nReason: {summary}{hint}",
                    cancellationToken);

                if (_stopOnRequiredFailure && action.IsRequired)
                {
                    await SendMessageAsync($"Halting setup because required step failed: {action.Label}.", cancellationToken);
                    break;
                }

                continue;
            }

            await SendMessageAsync($"Step {stepNo} completed.", cancellationToken);
        }

        var final = new List<string>
        {
            $"AI setup finished for {plan.Technology}.",
            failures.Any() ? "Completed with failures:" : "All planned steps completed."
        };

        if (failures.Any())
        {
            final.AddRange(failures.Select(f => "- " + f));
        }

        final.Add("Send your technology again for a fresh post-change analysis.");

        _pendingPlan = null;
        await SendMessageAsync(string.Join("\n", final), cancellationToken);
    }

    private string BuildReadinessMessage(PendingSetupPlan plan)
    {
        var lines = new List<string>
        {
            $"AI setup plan for: {plan.Technology}",
            $"Platform: {(OperatingSystem.IsWindows() ? "Windows" : "Linux")}",
            string.Empty,
            "Summary:",
            plan.Summary
        };

        if (plan.Prerequisites.Any())
        {
            lines.Add(string.Empty);
            lines.Add("Detected prerequisites:");
            foreach (var p in plan.Prerequisites)
            {
                var type = p.Required ? "required" : "recommended";
                var evidence = string.IsNullOrWhiteSpace(p.Evidence) ? string.Empty : $" ({p.Evidence})";
                lines.Add($"- {p.Name} [{type}]: {p.Status}{evidence}");
            }
        }

        lines.Add(string.Empty);
        lines.Add($"Planned setup steps: {plan.InstallActions.Count}");
        lines.Add(plan.InstallActions.Any()
            ? "Reply `okay` to run setup commands now."
            : "No executable steps were returned by AI.");

        if (OperatingSystem.IsLinux() && !_useSudoForInstallCommands && !IsRunningAsRoot())
        {
            lines.Add("Note: service is non-root and sudo mode is disabled; privileged commands may fail.");
        }

        if (plan.Notes.Any())
        {
            lines.Add(string.Empty);
            lines.Add("AI notes:");
            lines.AddRange(plan.Notes.Select(n => "- " + n));
        }

        lines.Add("Reply `steps` to view commands, or `cancel` to discard this plan.");
        return string.Join("\n", lines);
    }

    private static string BuildManualStepsMessage(PendingSetupPlan plan)
    {
        var lines = new List<string> { $"AI setup commands for {plan.Technology}:" };
        if (!plan.InstallActions.Any())
        {
            lines.Add("- No executable commands available.");
            return string.Join("\n", lines);
        }

        for (var i = 0; i < plan.InstallActions.Count; i++)
        {
            var action = plan.InstallActions[i];
            var type = action.IsRequired ? "required" : "recommended";
            lines.Add($"{i + 1}. {action.Label} [{type}]");
            lines.Add($"   {action.Command}");
        }

        return string.Join("\n", lines);
    }

    private static bool IsHelpMessage(string text) =>
        string.Equals(text, "/start", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "/help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "help", StringComparison.OrdinalIgnoreCase);

    private static bool IsOkayMessage(string text)
    {
        var value = text.Trim().TrimEnd('.', '!', '?').ToLowerInvariant();
        return value is "ok" or "okay" or "yes" or "start" or "go ahead";
    }

    private static bool IsCancelMessage(string text)
    {
        var value = text.Trim().ToLowerInvariant();
        return value is "cancel" or "stop" or "abort";
    }

    private static string BuildHelpMessage() =>
        "Send your app technology and I will generate an AI setup plan from current server analysis.\n" +
        "Examples:\n- My app is Django with Postgres\n- We use Next.js\n- I need to host FastAPI\n\n" +
        "Then reply `okay` to execute, `steps` to view commands, or `cancel`.";

    private static string BuildServerSnapshotForPrompt()
    {
        var lines = new List<string>
        {
            $"GeneratedAtUtc: {DateTimeOffset.UtcNow:O}",
            $"Platform: {(OperatingSystem.IsWindows() ? "Windows" : "Linux")}"
        };

        if (OperatingSystem.IsLinux())
        {
            lines.Add(ReadProbe("Kernel", "uname -srm", 5000));
            lines.Add(ReadProbe("CurrentUser", "whoami", 3000));
            lines.Add(ReadProbe("UserId", "id -u", 3000));
            lines.Add(ReadProbe("OsRelease", "cat /etc/os-release | sed -n '1,6p'", 5000, true));
            lines.Add(ReadProbe("PackageManagers", "for c in apt-get apt dnf yum pacman zypper apk; do if command -v \"$c\" >/dev/null 2>&1; then echo \"$c\"; fi; done", 5000, true));
            lines.Add(ReadProbe("InstalledRuntimes", "for c in node npm pnpm yarn bun dotnet java python3 go docker nginx pm2 caddy apache2; do if command -v \"$c\" >/dev/null 2>&1; then printf \"%s=\" \"$c\"; ($c --version 2>/dev/null || $c -v 2>/dev/null || echo installed) | head -n 1; fi; done", 8000, true));
            lines.Add(ReadProbe("Systemd", "systemctl --version | head -n 1", 4000));
            lines.Add(ReadProbe("Memory", "free -h | sed -n '1,2p'", 4000, true));
            lines.Add(ReadProbe("DiskRoot", "df -h / | tail -n 1", 4000));
        }
        else
        {
            lines.Add(ReadProbe("WindowsVersion", "[System.Environment]::OSVersion.VersionString", 5000));
            lines.Add(ReadProbe("CurrentUser", "$env:USERNAME", 3000));
            lines.Add(ReadProbe("PackageManager", "if (Get-Command winget -ErrorAction SilentlyContinue) { 'winget' } else { 'none' }", 4000));
            lines.Add(ReadProbe("Memory", "try{ $m=Get-CimInstance Win32_OperatingSystem -ErrorAction Stop; \\\"Total: $([math]::Round($m.TotalVisibleMemorySize / 1024)) MB, Free: $([math]::Round($m.FreePhysicalMemory / 1024)) MB\\\" }catch{ \\\"unknown\\\" }", 5000));
        }

        var snapshot = string.Join("\n", lines);
        return snapshot.Length <= 5000 ? snapshot : snapshot[..5000] + "\n[Server snapshot truncated]";
    }

    private static string BuildPlannerPrompt(string userRequest, string serverSnapshot)
    {
        return
            "You are a DevOps expert. Create a deployment plan.\n" +
            "Return ONLY valid JSON. No markdown blocks.\n" +
            "Schema: {\"technology\":\"string\",\"prerequisites\":[{\"name\":\"string\",\"required\":true,\"status\":\"installed|missing\"}],\"steps\":[{\"title\":\"string\",\"command\":\"shell_command\",\"required\":true}]}\n" +
            "Rules:\n" +
            "1. Use detected package manager.\n" +
            "2. Commands must be non-interactive.\n" +
            "3. Skip installed tools.\n" +
            "4. No destructive commands.\n" +
            "5. Ensure recommended stack fits strictly within the system's available CPU and Memory constraints provided in the Snapshot.\n" +
            "6. Focus strictly on the requested technology. DO NOT containerize or use Docker unless explicitly requested in the prompt.\n" +
            $"Request: {userRequest}\n" +
            $"Snapshot:\n{serverSnapshot}";
    }

    private static bool TryParseAiPlan(string response, string userRequest, out ParsedAiPlan plan, out string error)
    {
        plan = default!;
        if (string.IsNullOrWhiteSpace(response))
        {
            error = "empty response";
            return false;
        }

        if (!TryExtractJsonObject(response, out var json))
        {
            error = "response is not JSON";
            return false;
        }

        AiPlanDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<AiPlanDto>(json, PlannerJsonOptions);
        }
        catch (Exception ex)
        {
            error = $"JSON parse failed ({ex.Message})";
            return false;
        }

        if (dto is null)
        {
            error = "JSON payload is null";
            return false;
        }

        var technology = FirstNonEmpty(dto.Technology, userRequest);
        var summary = FirstNonEmpty(dto.Summary, "AI generated setup plan.");

        var prerequisites = (dto.Prerequisites ?? new List<AiPrerequisiteDto>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new PrerequisiteStatus(
                p.Name!.Trim(),
                p.Required ?? true,
                NormalizeStatus(p.Status),
                p.Evidence?.Trim() ?? string.Empty))
            .ToList();

        var notes = (dto.Notes ?? new List<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actions = new List<InstallAction>();
        foreach (var step in dto.Steps ?? new List<AiStepDto>())
        {
            var title = FirstNonEmpty(step.Title, "Setup step");
            var command = NormalizeCommand(step.Command);
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            if (IsPotentiallyDestructive(command))
            {
                notes.Add($"Skipped potentially destructive command from AI: {title}");
                continue;
            }

            actions.Add(new InstallAction(title, command, step.Required ?? true));
        }

        if (actions.Count == 0)
        {
            error = "AI returned no executable commands";
            return false;
        }

        plan = new ParsedAiPlan(technology, summary, prerequisites, actions, notes);
        error = string.Empty;
        return true;
    }

    private async Task<IReadOnlyList<TelegramInboundMessage>> GetUpdatesAsync(CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Clamp((int)_pollInterval.TotalSeconds, 1, 30);
        var endpoint = $"https://api.telegram.org/bot{_token}/getUpdates?offset={_nextUpdateId}&limit=20&timeout={timeoutSeconds}&allowed_updates=%5B%22message%22%5D";

        using var response = await HttpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<TelegramInboundMessage>();
        }

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            return Array.Empty<TelegramInboundMessage>();
        }

        if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TelegramInboundMessage>();
        }

        var updates = new List<TelegramInboundMessage>();
        foreach (var updateElement in result.EnumerateArray())
        {
            if (!updateElement.TryGetProperty("update_id", out var updateIdElement) ||
                !updateIdElement.TryGetInt64(out var updateId))
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

            if (!messageElement.TryGetProperty("text", out var textElement))
            {
                continue;
            }

            var text = textElement.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var chatId = chatIdElement.ValueKind switch
            {
                JsonValueKind.Number => chatIdElement.GetInt64().ToString(CultureInfo.InvariantCulture),
                JsonValueKind.String => chatIdElement.GetString() ?? string.Empty,
                _ => string.Empty
            };

            updates.Add(new TelegramInboundMessage(updateId, chatId, text));
        }

        return updates;
    }

    private async Task<int?> SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var safe = message.Length > 3900 ? message[..3875] + "\n\n[Message truncated]" : message;
        using var response = await HttpClient.PostAsync(
            $"https://api.telegram.org/bot{_token}/sendMessage",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("chat_id", _chatId),
                new KeyValuePair<string, string>("text", safe),
                new KeyValuePair<string, string>("parse_mode", "Markdown")
            ]),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        
        var payload = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.TryGetProperty("result", out var result) && 
            result.TryGetProperty("message_id", out var id))
        {
            return id.GetInt32();
        }
        return null;
    }

    private async Task EditMessageAsync(int messageId, string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var safe = message.Length > 3900 ? message[..3875] + "\n\n[Message truncated]" : message;
        using var response = await HttpClient.PostAsync(
            $"https://api.telegram.org/bot{_token}/editMessageText",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("chat_id", _chatId),
                new KeyValuePair<string, string>("message_id", messageId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("text", safe),
                new KeyValuePair<string, string>("parse_mode", "Markdown")
            ]),
            cancellationToken);

        // We don't throw on edit failure because it might be due to "message is not modified" which is fine
    }

    private string BuildExecutionCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || !OperatingSystem.IsLinux() || !_useSudoForInstallCommands || IsRunningAsRoot())
        {
            return command;
        }

        var escaped = EscapeForDoubleQuotedBash(command);
        return $"sudo /bin/bash -lc \"{escaped}\"";
    }

    private static bool ShouldSuggestSudo(string errorSummary)
    {
        if (string.IsNullOrWhiteSpace(errorSummary))
        {
            return false;
        }

        return errorSummary.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
               errorSummary.Contains("could not open lock file", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRunningAsRoot()
    {
        var user = Environment.GetEnvironmentVariable("USER") ??
                   Environment.GetEnvironmentVariable("USERNAME") ??
                   Environment.UserName;
        return string.Equals(user, "root", StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeForDoubleQuotedBash(string command) =>
        command.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);

    private static string ReadProbe(string label, string command, int timeoutMs, bool multiline = false)
    {
        var result = RunCommand(command, timeoutMs);
        if (result.TimedOut)
        {
            return $"{label}: timed out";
        }

        if (result.ExitCode != 0)
        {
            return $"{label}: unavailable ({FirstNonEmptyLine(result.StdErr, result.StdOut)})";
        }

        var output = string.IsNullOrWhiteSpace(result.StdOut) ? result.StdErr : result.StdOut;
        if (string.IsNullOrWhiteSpace(output))
        {
            return $"{label}: available";
        }

        if (!multiline)
        {
            return $"{label}: {FirstNonEmptyLine(output)}";
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8);
        return $"{label}:\n{string.Join("\n", lines)}";
    }

    private static CommandResult RunCommand(string command, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new CommandResult(-1, string.Empty, "Command is empty.", false);
        }

        using var process = new Process { StartInfo = CreateProcessStartInfo(command) };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore kill errors
            }

            var timeoutStdout = stdoutTask.IsCompleted ? stdoutTask.GetAwaiter().GetResult() : string.Empty;
            var timeoutStderr = stderrTask.IsCompleted ? stderrTask.GetAwaiter().GetResult() : string.Empty;
            return new CommandResult(-1, timeoutStdout.Trim(), timeoutStderr.Trim(), true);
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
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

    private static bool TryExtractJsonObject(string raw, out string json)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            json = string.Empty;
            return false;
        }

        json = raw[start..(end + 1)];
        return true;
    }

    private static string NormalizeStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "unknown";
        }

        var value = raw.Trim().ToLowerInvariant();
        return value switch
        {
            "installed" or "present" or "ok" or "available" => "installed",
            "missing" or "absent" or "not installed" => "missing",
            _ => "unknown"
        };
    }

    private static string NormalizeCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var normalized = command
            .Replace("```bash", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("```", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        return string.Join(" ", normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();
    }

    private static bool IsPotentiallyDestructive(string command)
    {
        var value = command.ToLowerInvariant();
        return value.Contains(" rm -rf ", StringComparison.Ordinal) ||
               value.StartsWith("rm -rf", StringComparison.Ordinal) ||
               value.Contains("mkfs", StringComparison.Ordinal) ||
               value.Contains("dd if=", StringComparison.Ordinal) ||
               value.Contains("shutdown", StringComparison.Ordinal) ||
               value.Contains("reboot", StringComparison.Ordinal) ||
               value.Contains("userdel", StringComparison.Ordinal) ||
               value.Contains("del /f /s /q", StringComparison.Ordinal);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string FirstNonEmptyLine(params string[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var line = value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return "No additional output.";
    }

    private sealed record TelegramInboundMessage(long UpdateId, string ChatId, string Text);
    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);
    private sealed record InstallAction(string Label, string Command, bool IsRequired);
    private sealed record PrerequisiteStatus(string Name, bool Required, string Status, string Evidence);
    private sealed record PendingSetupPlan(
        string Technology,
        string Summary,
        IReadOnlyList<PrerequisiteStatus> Prerequisites,
        IReadOnlyList<InstallAction> InstallActions,
        IReadOnlyList<string> Notes,
        DateTimeOffset RequestedAtUtc);
    private sealed record ParsedAiPlan(
        string Technology,
        string Summary,
        IReadOnlyList<PrerequisiteStatus> Prerequisites,
        IReadOnlyList<InstallAction> InstallActions,
        IReadOnlyList<string> Notes);

    private sealed class AiPlanDto
    {
        public string? Technology { get; set; }
        public string? Summary { get; set; }
        public List<AiPrerequisiteDto>? Prerequisites { get; set; }
        public List<AiStepDto>? Steps { get; set; }
        public List<string>? Notes { get; set; }
    }

    private sealed class AiPrerequisiteDto
    {
        public string? Name { get; set; }
        public bool? Required { get; set; }
        public string? Status { get; set; }
        public string? Evidence { get; set; }
    }

    private sealed class AiStepDto
    {
        public string? Title { get; set; }
        public string? Command { get; set; }
        public bool? Required { get; set; }
    }
}
