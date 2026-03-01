using System.Globalization;
using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;
using DeploymentGuardian.Modules;
using DeploymentGuardian.Rules;
using DeploymentGuardian.Services;
using DeploymentGuardian.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("Config/guardian.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "GUARDIAN_")
    .Build();

var loadedOptions = LoadOptions(configuration);
ValidateOptions(loadedOptions);
var options = NormalizeOptions(loadedOptions);
var runInterval = ResolveRunInterval(args, options);
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Information);
    builder.AddSimpleConsole(console =>
    {
        console.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
        console.SingleLine = true;
    });
});
var logger = loggerFactory.CreateLogger("DeploymentGuardian");
var runId = Guid.NewGuid().ToString("N")[..8];
using var runScope = logger.BeginScope(new Dictionary<string, object> { ["runId"] = runId });
using var shutdownCts = new CancellationTokenSource();
var shutdownToken = shutdownCts.Token;

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdownCts.Cancel();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    if (!shutdownCts.IsCancellationRequested)
    {
        try
        {
            shutdownCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignore process-exit race where token source is already disposed.
        }
    }
};

IShellHelper shell = new ShellHelper();
IServerDataCollector serverAnalyzer;
IProcessDataCollector processAnalyzer;
ISecurityDataCollector securityAnalyzer;
IServiceDataCollector serviceScanner;

if (OperatingSystem.IsLinux())
{
    serverAnalyzer = new ServerAnalyzer(shell);
    processAnalyzer = new ProcessAnalyzer(shell);
    securityAnalyzer = new SecurityScanner(shell);
    serviceScanner = new ServiceScanner(shell);
    logger.LogInformation("Using Linux collectors.");
}
else if (OperatingSystem.IsWindows())
{
    serverAnalyzer = new WindowsServerCollector();
    processAnalyzer = new WindowsProcessCollector();
    securityAnalyzer = new WindowsSecurityCollector();
    serviceScanner = new WindowsServiceCollector();
    logger.LogInformation("Using Windows collectors.");
}
else
{
    throw new PlatformNotSupportedException(
        $"Unsupported platform: {Environment.OSVersion.Platform}. Supported platforms are Linux and Windows.");
}
var memoryTrend = new MemoryTrendAnalyzer(options.MemoryLogFilePath);
var riskCalculator = new RiskCalculator(
    options.RiskRamPercent,
    options.MaxOpenPortsWarningCount,
    options.ProcessCpuCriticalPercent);

var appAnalyzer = new AppAnalyzer();
var appProfile = appAnalyzer.Analyze();
var aiAdvisor = BuildAiAdvisor(options);

var rules = new List<IRule<ServerContext>>
{
    new ScalingRules(options.CpuSpikeMultiplier, options.DiskUsageWarningPercent),
    new MemoryRules(options.RamUsageWarningPercent),
    new SecurityRules(options.MaxOpenPortsWarningCount),
    new ProcessRules(options.ProcessCpuCriticalPercent, options.ProcessMemoryWarningPercent),
    new ServiceRules()
};
var engine = new RequirementEngine(rules);

var alertDeduplicator = new AlertDeduplicator(
    options.AlertStateFilePath,
    TimeSpan.FromMinutes(options.AlertCooldownMinutes));
var historyStore = new AlertHistoryStore(
    options.AlertHistoryFilePath,
    options.AlertHistoryMaxEntries);

var notifier = BuildNotifier(options);

if (runInterval <= TimeSpan.Zero)
{
    logger.LogInformation("Deployment Guardian started in single-run mode.");
    await ExecuteCycleAsync(
        serverAnalyzer,
        processAnalyzer,
        securityAnalyzer,
        serviceScanner,
        memoryTrend,
        riskCalculator,
        engine,
        alertDeduplicator,
        historyStore,
        notifier,
        aiAdvisor,
        appProfile,
        options,
        logger,
        1,
        shutdownToken);

    logger.LogInformation("Single-run execution complete.");
    return;
}

logger.LogInformation("Deployment Guardian started in interval mode. Interval={Interval}", runInterval);
var cycleNumber = 0;

while (!shutdownToken.IsCancellationRequested)
{
    cycleNumber++;
    await ExecuteCycleAsync(
        serverAnalyzer,
        processAnalyzer,
        securityAnalyzer,
        serviceScanner,
        memoryTrend,
        riskCalculator,
        engine,
        alertDeduplicator,
        historyStore,
        notifier,
        aiAdvisor,
        appProfile,
        options,
        logger,
        cycleNumber,
        shutdownToken);

    try
    {
        await Task.Delay(runInterval, shutdownToken);
    }
    catch (OperationCanceledException)
    {
        break;
    }
}

logger.LogInformation("Deployment Guardian stopped.");

/// <summary>
/// Executes one full monitoring cycle and sends alerts when needed.
/// </summary>
static async Task ExecuteCycleAsync(
    IServerDataCollector serverAnalyzer,
    IProcessDataCollector processAnalyzer,
    ISecurityDataCollector securityAnalyzer,
    IServiceDataCollector serviceScanner,
    MemoryTrendAnalyzer memoryTrend,
    RiskCalculator riskCalculator,
    RequirementEngine engine,
    AlertDeduplicator alertDeduplicator,
    AlertHistoryStore historyStore,
    INotifier? notifier,
    IAiAdvisor? aiAdvisor,
    AppAnalysisResult appProfile,
    GuardianOptions options,
    ILogger logger,
    int cycleNumber,
    CancellationToken cancellationToken)
{
    using var cycleScope = logger.BeginScope(new Dictionary<string, object> { ["cycle"] = cycleNumber });
    var nowUtc = DateTimeOffset.UtcNow;
    var context = new ServerContext
    {
        Metrics = new ServerMetrics(),
        Processes = new List<ProcessInfo>(),
        Security = new SecurityReport(),
        Services = new ServiceReport()
    };
    var report = new DeploymentReport();
    var sentAlerts = 0;
    var suppressedCount = 0;

    try
    {
        cancellationToken.ThrowIfCancellationRequested();

        context = new ServerContext
        {
            Metrics = serverAnalyzer.Analyze(),
            Processes = processAnalyzer.GetTopProcesses(),
            Security = securityAnalyzer.Analyze(),
            Services = serviceScanner.Scan()
        };

        var alerts = engine.Evaluate(context);

        if (memoryTrend.IsMemoryIncreasing(
                context.Metrics.RamUsagePercent,
                options.MemoryTrendSamples,
                options.MemoryTrendAlertPercent))
        {
            alerts.Add(new Alert
            {
                Code = AlertCodes.MemoryLeakTrend,
                Message = "Possible memory leak trend detected.",
                Severity = AlertSeverity.Warning
            });
        }

        var risk = riskCalculator.Calculate(context);
        report = new DeploymentReport
        {
            Alerts = alerts,
            RiskScore = risk
        };

        nowUtc = DateTimeOffset.UtcNow;
        if (!alerts.Any())
        {
            historyStore.Append(BuildHistoryEntry(report, context, nowUtc, 0, 0, true, null));
            logger.LogInformation("No alerts.");
            return;
        }

        var alertsToSend = alertDeduplicator.FilterSendable(report.Alerts, nowUtc);
        suppressedCount = report.Alerts.Count - alertsToSend.Count;

        if (!alertsToSend.Any())
        {
            historyStore.Append(BuildHistoryEntry(report, context, nowUtc, 0, suppressedCount, true, null));
            logger.LogInformation(
                "{AlertCount} alert(s) generated, all suppressed by cooldown.",
                report.Alerts.Count);
            return;
        }

        var sendableReport = new DeploymentReport
        {
            Alerts = alertsToSend,
            RiskScore = report.RiskScore
        };

        var message = BuildAlertMessage(sendableReport, suppressedCount, nowUtc, context, appProfile);
        message = await AppendAiSuggestionsAsync(
            message,
            aiAdvisor,
            sendableReport,
            context,
            appProfile,
            cancellationToken);
        message = TrimForTelegram(message, 3900);

        if (notifier is null)
        {
            historyStore.Append(BuildHistoryEntry(report, context, nowUtc, 0, suppressedCount, false, "Notifier not configured"));
            logger.LogWarning("Alerts detected but no notification channel is configured.");
            logger.LogInformation("Alert payload:\n{Message}", message);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await notifier.SendAsync(message);
        alertDeduplicator.MarkSent(sendableReport.Alerts, nowUtc);
        sentAlerts = sendableReport.Alerts.Count;
        historyStore.Append(BuildHistoryEntry(
            report,
            context,
            nowUtc,
            sentAlerts,
            suppressedCount,
            true,
            null));
        logger.LogInformation(
            "Sent {AlertCount} alert(s). RiskScore={RiskScore}",
            sendableReport.Alerts.Count,
            sendableReport.RiskScore);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        logger.LogInformation("Scan cancelled.");
    }
    catch (Exception ex)
    {
        historyStore.Append(BuildHistoryEntry(
            report,
            context,
            nowUtc,
            sentAlerts,
            suppressedCount,
            false,
            ex.Message));
        logger.LogError(ex, "Cycle failed.");
    }
}

/// <summary>
/// Builds the outbound alert payload text with summary context.
/// </summary>
static string BuildAlertMessage(
    DeploymentReport report,
    int suppressedCount,
    DateTimeOffset generatedAtUtc,
    ServerContext context,
    AppAnalysisResult appProfile)
{
    var criticalCount = report.Alerts.Count(a => a.Severity == AlertSeverity.Critical);
    var warningCount = report.Alerts.Count(a => a.Severity == AlertSeverity.Warning);
    var infoCount = report.Alerts.Count(a => a.Severity == AlertSeverity.Info);
    var cooldownLine = suppressedCount > 0 ? $"Suppressed by cooldown: {suppressedCount}" : "Suppressed by cooldown: 0";

    var summaryBlock =
        $"CPU Load: {context.Metrics.CpuLoad:F2} (cores: {context.Metrics.CpuCores})\n" +
        $"RAM Usage: {context.Metrics.RamUsagePercent:F1}%\n" +
        $"Disk Usage: {context.Metrics.DiskUsagePercent:F1}%\n" +
        $"Open Ports: {context.Security.OpenPorts.Count}\n" +
        $"Running Services: {context.Services.RunningServices.Count}\n" +
        $"Top Processes Tracked: {context.Processes.Count}\n" +
        $"App Profile: {appProfile.AppType}, DB={appProfile.UsesDatabase}, BGJobs={appProfile.UsesBackgroundServices}";

    var topProcessBlock = BuildTopProcessBlock(context.Processes);
    var detailedAlerts = BuildDetailedAlertsBlock(report.Alerts, context);
    var fixBlock = BuildFixActionsBlock(report.Alerts);

    var message =
        "Deployment Guardian Alert\n\n" +
        $"Generated At (UTC): {FormatReadableDateTime(generatedAtUtc)}\n" +
        $"Risk Score: {report.RiskScore}/100\n" +
        $"Severity Summary: Critical={criticalCount}, Warning={warningCount}, Info={infoCount}\n" +
        $"{cooldownLine}\n\n" +
        $"System Snapshot:\n{summaryBlock}\n\n" +
        $"Top Processes:\n{topProcessBlock}\n\n" +
        $"Alert Details:\n{detailedAlerts}\n\n" +
        $"Suggested Fix Actions:\n{fixBlock}";

    return message;
}

/// <summary>
/// Formats UTC timestamp in a human-readable date/time style.
/// </summary>
static string FormatReadableDateTime(DateTimeOffset timestampUtc)
{
    return timestampUtc.UtcDateTime.ToString("dd/MMM/yyyy : hh:mm:ss tt", CultureInfo.InvariantCulture);
}

/// <summary>
/// Builds a small process snapshot for quick operator triage.
/// </summary>
static string BuildTopProcessBlock(IEnumerable<ProcessInfo> processes)
{
    var top = processes.Take(3).ToList();
    if (!top.Any())
    {
        return "- No process samples available";
    }

    return string.Join(
        "\n",
        top.Select(p => $"- {p.Name}: CPU {p.Cpu:F1}%, MEM {p.Memory:F1}%"));
}

/// <summary>
/// Builds per-alert details with evidence from current runtime context.
/// </summary>
static string BuildDetailedAlertsBlock(IEnumerable<Alert> alerts, ServerContext context)
{
    var lines = new List<string>();
    var index = 1;

    foreach (var alert in alerts)
    {
        lines.Add($"{index}. [{alert.Severity}] [{alert.Code}] {alert.Message}");
        var evidence = GetAlertEvidence(alert, context);
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            lines.Add($"   Evidence: {evidence}");
        }

        index++;
    }

    return string.Join("\n", lines);
}

/// <summary>
/// Builds unique remediation actions mapped from current alerts.
/// </summary>
static string BuildFixActionsBlock(IEnumerable<Alert> alerts)
{
    var actions = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var alert in alerts)
    {
        var action = GetFixAction(alert);
        if (seen.Add(action))
        {
            actions.Add($"- {action}");
        }
    }

    return actions.Any() ? string.Join("\n", actions) : "- Investigate host logs and rerun health scan.";
}

/// <summary>
/// Provides evidence text for a specific alert based on current metrics.
/// </summary>
static string GetAlertEvidence(Alert alert, ServerContext context)
{
    return alert.Code switch
    {
        AlertCodes.CpuSpike => $"Load={context.Metrics.CpuLoad:F2}, cores={context.Metrics.CpuCores}",
        AlertCodes.ProcessCpuHigh => BuildProcessEvidence(context, true),
        AlertCodes.ProcessMemoryHigh => BuildProcessEvidence(context, false),
        AlertCodes.RamUsageHigh => $"RAM usage={context.Metrics.RamUsagePercent:F1}%",
        AlertCodes.MemoryLeakTrend => $"Current RAM usage={context.Metrics.RamUsagePercent:F1}%",
        AlertCodes.DiskUsageHigh => $"Disk usage={context.Metrics.DiskUsagePercent:F1}%",
        AlertCodes.OpenPortsHigh => $"Open ports count={context.Security.OpenPorts.Count}; sample={string.Join(", ", context.Security.OpenPorts.Take(10))}",
        AlertCodes.RootLoginEnabled => $"RootLoginDisabled={context.Security.RootLoginDisabled}",
        AlertCodes.SshPasswordAuthEnabled => $"PasswordAuthDisabled={context.Security.PasswordAuthDisabled}",
        AlertCodes.Fail2BanInactive => $"Fail2Ban active={context.Security.Fail2BanInstalled}",
        AlertCodes.SshServiceMissing => $"SSH service running={HasSshService(context.Services.RunningServices)}",
        AlertCodes.ServicesNoneRunning => $"Running services count={context.Services.RunningServices.Count}",
        _ => string.Empty
    };
}

/// <summary>
/// Maps each alert category to a concrete operator fix action.
/// </summary>
static string GetFixAction(Alert alert)
{
    return alert.Code switch
    {
        AlertCodes.ProcessCpuHigh => "Inspect hot process (`top -H -p <pid>`), check recent deploy/log spikes, then scale out or restart the service if runaway.",
        AlertCodes.CpuSpike => "Check CPU bottleneck (`uptime`, `mpstat`), reduce burst traffic, and increase compute capacity or autoscaling threshold.",
        AlertCodes.ProcessMemoryHigh => "Capture memory profile (`dotnet-gcdump`/`dotnet-dump`), restart leaking process, and review cache/object retention settings.",
        AlertCodes.RamUsageHigh => "Capture memory profile (`dotnet-gcdump`/`dotnet-dump`), restart leaking process, and review cache/object retention settings.",
        AlertCodes.DiskUsageHigh => "Free disk space (`du -sh /*`, prune logs/artifacts), expand volume if needed, and enable log rotation.",
        AlertCodes.FirewallDisabled => "Enable and enforce firewall rules (`ufw enable` + allow required ports only).",
        AlertCodes.RootLoginEnabled => "Set `PermitRootLogin no`, restart SSH daemon, and use sudo-based non-root accounts.",
        AlertCodes.SshPasswordAuthEnabled => "Set `PasswordAuthentication no`, enforce SSH key auth, then restart SSH daemon.",
        AlertCodes.Fail2BanInactive => "Install/enable Fail2Ban (`apt install fail2ban`, `systemctl enable --now fail2ban`) and verify jail config.",
        AlertCodes.OpenPortsHigh => "Review listening services (`ss -tulpn`), close unused ports, and restrict exposure with firewall/security groups.",
        AlertCodes.ServicesNoneRunning => "Verify systemd health (`systemctl --failed`), recover critical units, and investigate host boot/runtime failures.",
        AlertCodes.SshServiceMissing => "Ensure SSH service is installed and running (`systemctl enable --now ssh` or `sshd`).",
        AlertCodes.MemoryLeakTrend => "Compare heap growth over time, inspect long-lived allocations, and deploy a fix for retention/leak sources.",
        _ => "Review logs and metrics for this alert, then patch the root cause and confirm by rerunning the scan."
    };
}

/// <summary>
/// Builds evidence line from top process data for CPU or memory driven process alerts.
/// </summary>
static string BuildProcessEvidence(ServerContext context, bool byCpu)
{
    var data = byCpu
        ? context.Processes.OrderByDescending(p => p.Cpu).Take(2).Select(p => $"{p.Name} CPU {p.Cpu:F1}%")
        : context.Processes.OrderByDescending(p => p.Memory).Take(2).Select(p => $"{p.Name} MEM {p.Memory:F1}%");

    return string.Join("; ", data);
}

/// <summary>
/// Checks whether SSH service appears in running service list.
/// </summary>
static bool HasSshService(IEnumerable<string> runningServices)
{
    return runningServices.Any(s =>
        string.Equals(s, "ssh.service", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s, "sshd.service", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s, "sshd", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s, "ssh", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Trims message length to stay under Telegram limits while preserving useful content.
/// </summary>
static string TrimForTelegram(string message, int maxLength)
{
    if (string.IsNullOrEmpty(message) || message.Length <= maxLength)
    {
        return message;
    }

    return message[..(maxLength - 32)] + "\n\n[Message truncated]";
}

/// <summary>
/// Adds AI suggestions to the alert message when advisor is enabled and available.
/// </summary>
static async Task<string> AppendAiSuggestionsAsync(
    string message,
    IAiAdvisor? aiAdvisor,
    DeploymentReport report,
    ServerContext context,
    AppAnalysisResult appProfile,
    CancellationToken cancellationToken)
{
    if (aiAdvisor is null)
    {
        return message;
    }

    try
    {
        cancellationToken.ThrowIfCancellationRequested();
        var summary = BuildAdvisorSummary(report, context, appProfile);
        var suggestions = await aiAdvisor.GetSuggestionsAsync(summary);

        if (string.IsNullOrWhiteSpace(suggestions))
        {
            return message;
        }

        return $"{message}\n\nAI Suggestions:\n{suggestions.Trim()}";
    }
    catch (Exception ex)
    {
        return $"{message}\n\nAI Suggestions: unavailable ({ex.Message})";
    }
}

/// <summary>
/// Builds compact context text for AI recommendation generation.
/// </summary>
static string BuildAdvisorSummary(
    DeploymentReport report,
    ServerContext context,
    AppAnalysisResult appProfile)
{
    var alertLines = string.Join("\n", report.Alerts.Select(a => $"- [{a.Severity}] {a.Message}"));
    var processLines = string.Join(
        "\n",
        context.Processes.Take(5).Select(p => $"- {p.Name}: CPU {p.Cpu:F1}%, MEM {p.Memory:F1}%"));

    return
        $"RiskScore: {report.RiskScore}\n" +
        $"AppType: {appProfile.AppType}\n" +
        $"UsesDatabase: {appProfile.UsesDatabase}\n" +
        $"UsesBackgroundServices: {appProfile.UsesBackgroundServices}\n" +
        $"ExpectedConcurrentUsers: {appProfile.ExpectedConcurrentUsers}\n" +
        $"CPU_Load: {context.Metrics.CpuLoad:F2}\n" +
        $"CPU_Cores: {context.Metrics.CpuCores}\n" +
        $"RAM_Usage_Percent: {context.Metrics.RamUsagePercent:F1}\n" +
        $"Disk_Usage_Percent: {context.Metrics.DiskUsagePercent:F1}\n" +
        $"OpenPortsCount: {context.Security.OpenPorts.Count}\n" +
        $"Fail2BanActive: {context.Security.Fail2BanInstalled}\n" +
        $"PasswordAuthDisabled: {context.Security.PasswordAuthDisabled}\n" +
        $"Alerts:\n{alertLines}\n" +
        $"TopProcesses:\n{processLines}\n" +
        "Provide concise action items prioritized by impact and urgency.";
}

/// <summary>
/// Builds a persisted history entry from current cycle result and host context.
/// </summary>
static AlertHistoryEntry BuildHistoryEntry(
    DeploymentReport report,
    ServerContext context,
    DateTimeOffset nowUtc,
    int sentAlerts,
    int suppressedAlerts,
    bool deliverySucceeded,
    string? deliveryError)
{
    return new AlertHistoryEntry
    {
        TimestampUtc = nowUtc,
        RiskScore = report.RiskScore,
        TotalAlerts = report.Alerts.Count,
        SentAlerts = sentAlerts,
        SuppressedAlerts = suppressedAlerts,
        DeliverySucceeded = deliverySucceeded,
        DeliveryError = deliveryError,
        CpuLoad = context.Metrics.CpuLoad,
        CpuCores = context.Metrics.CpuCores,
        RamUsagePercent = context.Metrics.RamUsagePercent,
        DiskUsagePercent = context.Metrics.DiskUsagePercent,
        OpenPortsCount = context.Security.OpenPorts.Count,
        RunningServicesCount = context.Services.RunningServices.Count,
        ProcessesTracked = context.Processes.Count,
        AlertCodes = report.Alerts.Select(a => a.Code).ToList()
    };
}

/// <summary>
/// Creates AI advisor instance from runtime configuration.
/// </summary>
static IAiAdvisor? BuildAiAdvisor(GuardianOptions options)
{
    if (options.EnableOpenAiSuggestions)
    {
        return new OpenAiAdvisor();
    }

    if (options.EnableOllamaSuggestions)
    {
        return new OllamaAdvisor(options.OllamaBaseUrl, options.OllamaModel);
    }

    return null;
}

/// <summary>
/// Creates notifier instance with retry policy when Telegram credentials are configured.
/// </summary>
static INotifier? BuildNotifier(GuardianOptions options)
{
    var notifiers = new List<INotifier>();
    var telegramToken = Environment.GetEnvironmentVariable("TELEGRAM_TOKEN");
    var telegramChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
    var webhookUrl = FirstNonEmpty(
        Environment.GetEnvironmentVariable("WEBHOOK_URL"),
        options.WebhookUrl);
    var webhookAuthHeader = FirstNonEmpty(
        Environment.GetEnvironmentVariable("WEBHOOK_AUTH_HEADER"),
        options.WebhookAuthHeader);
    var webhookAuthValue = Environment.GetEnvironmentVariable("WEBHOOK_AUTH_VALUE");

    if (!string.IsNullOrWhiteSpace(telegramToken) && !string.IsNullOrWhiteSpace(telegramChatId))
    {
        notifiers.Add(new RetryingNotifier(
            new TelegramNotifier(telegramToken, telegramChatId),
            options.NotificationMaxAttempts,
            TimeSpan.FromSeconds(options.NotificationBaseDelaySeconds)));
    }

    if (!string.IsNullOrWhiteSpace(webhookUrl))
    {
        notifiers.Add(new RetryingNotifier(
            new WebhookNotifier(webhookUrl, webhookAuthHeader, webhookAuthValue),
            options.NotificationMaxAttempts,
            TimeSpan.FromSeconds(options.NotificationBaseDelaySeconds)));
    }

    return notifiers.Count switch
    {
        0 => null,
        1 => notifiers[0],
        _ => new MultiNotifier(notifiers)
    };
}

/// <summary>
/// Loads raw options from configuration sources without range validation.
/// </summary>
static GuardianOptions LoadOptions(IConfiguration configuration)
{
    return new GuardianOptions
    {
        MemoryLogFilePath = GetString(configuration, "MemoryLogFilePath", "/tmp/guardian-memory.log"),
        AlertStateFilePath = GetString(configuration, "AlertStateFilePath", "/tmp/guardian-alert-state.json"),
        AlertHistoryFilePath = GetString(configuration, "AlertHistoryFilePath", "/tmp/guardian-history.jsonl"),
        AlertHistoryMaxEntries = GetInt(configuration, "AlertHistoryMaxEntries", 5000),
        WebhookUrl = GetString(configuration, "WebhookUrl", string.Empty),
        WebhookAuthHeader = GetString(configuration, "WebhookAuthHeader", "Authorization"),
        EnableOllamaSuggestions = GetBool(configuration, "EnableOllamaSuggestions", false),
        OllamaBaseUrl = GetString(configuration, "OllamaBaseUrl", "http://localhost:11434"),
        OllamaModel = GetString(configuration, "OllamaModel", "llama3.2"),
        CpuSpikeMultiplier = GetDouble(configuration, "CpuSpikeMultiplier", 1.5),
        DiskUsageWarningPercent = GetDouble(configuration, "DiskUsageWarningPercent", 85),
        RamUsageWarningPercent = GetDouble(configuration, "RamUsageWarningPercent", 85),
        ProcessCpuCriticalPercent = GetDouble(configuration, "ProcessCpuCriticalPercent", 85),
        ProcessMemoryWarningPercent = GetDouble(configuration, "ProcessMemoryWarningPercent", 30),
        MaxOpenPortsWarningCount = GetInt(configuration, "MaxOpenPortsWarningCount", 20),
        MemoryTrendAlertPercent = GetDouble(configuration, "MemoryTrendAlertPercent", 70),
        MemoryTrendSamples = GetInt(configuration, "MemoryTrendSamples", 5),
        RiskRamPercent = GetDouble(configuration, "RiskRamPercent", 80),
        AlertCooldownMinutes = GetDouble(configuration, "AlertCooldownMinutes", 30),
        NotificationMaxAttempts = GetInt(configuration, "NotificationMaxAttempts", 3),
        NotificationBaseDelaySeconds = GetInt(configuration, "NotificationBaseDelaySeconds", 2),
        ScanIntervalSeconds = GetInt(configuration, "ScanIntervalSeconds", 0),
        EnableOpenAiSuggestions = GetBool(configuration, "EnableOpenAiSuggestions", false)
    };
}

/// <summary>
/// Validates configuration options and fails fast on unsupported values.
/// </summary>
static void ValidateOptions(GuardianOptions options)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(options.MemoryLogFilePath))
    {
        errors.Add("MemoryLogFilePath is required.");
    }

    if (string.IsNullOrWhiteSpace(options.AlertStateFilePath))
    {
        errors.Add("AlertStateFilePath is required.");
    }

    if (string.IsNullOrWhiteSpace(options.AlertHistoryFilePath))
    {
        errors.Add("AlertHistoryFilePath is required.");
    }

    ValidateRange(options.CpuSpikeMultiplier, 1.0, 10.0, nameof(options.CpuSpikeMultiplier), errors);
    ValidateRange(options.DiskUsageWarningPercent, 1, 100, nameof(options.DiskUsageWarningPercent), errors);
    ValidateRange(options.RamUsageWarningPercent, 1, 100, nameof(options.RamUsageWarningPercent), errors);
    ValidateRange(options.ProcessCpuCriticalPercent, 1, 100, nameof(options.ProcessCpuCriticalPercent), errors);
    ValidateRange(options.ProcessMemoryWarningPercent, 1, 100, nameof(options.ProcessMemoryWarningPercent), errors);
    ValidateIntRange(options.MaxOpenPortsWarningCount, 1, 1000, nameof(options.MaxOpenPortsWarningCount), errors);
    ValidateIntRange(options.AlertHistoryMaxEntries, 1, 200000, nameof(options.AlertHistoryMaxEntries), errors);
    ValidateRange(options.MemoryTrendAlertPercent, 1, 100, nameof(options.MemoryTrendAlertPercent), errors);
    ValidateIntRange(options.MemoryTrendSamples, 2, 100, nameof(options.MemoryTrendSamples), errors);
    ValidateRange(options.RiskRamPercent, 1, 100, nameof(options.RiskRamPercent), errors);
    ValidateRange(options.AlertCooldownMinutes, 0, 1440, nameof(options.AlertCooldownMinutes), errors);
    ValidateIntRange(options.NotificationMaxAttempts, 1, 10, nameof(options.NotificationMaxAttempts), errors);
    ValidateIntRange(options.NotificationBaseDelaySeconds, 1, 60, nameof(options.NotificationBaseDelaySeconds), errors);
    ValidateIntRange(options.ScanIntervalSeconds, 0, 86400, nameof(options.ScanIntervalSeconds), errors);

    if (!string.IsNullOrWhiteSpace(options.WebhookUrl) &&
        !IsValidHttpUrl(options.WebhookUrl))
    {
        errors.Add("WebhookUrl must be an absolute http/https URL.");
    }

    if (options.EnableOpenAiSuggestions && options.EnableOllamaSuggestions)
    {
        errors.Add("EnableOpenAiSuggestions and EnableOllamaSuggestions cannot both be true.");
    }

    if (options.EnableOpenAiSuggestions &&
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
    {
        errors.Add("EnableOpenAiSuggestions=true requires OPENAI_API_KEY environment variable.");
    }

    if (options.EnableOllamaSuggestions)
    {
        if (!IsValidHttpUrl(options.OllamaBaseUrl))
        {
            errors.Add("EnableOllamaSuggestions=true requires a valid OllamaBaseUrl http/https URL.");
        }

        if (string.IsNullOrWhiteSpace(options.OllamaModel))
        {
            errors.Add("EnableOllamaSuggestions=true requires OllamaModel.");
        }
    }

    if (errors.Any())
    {
        throw new InvalidOperationException(
            "Invalid guardian configuration:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "- " + e)));
    }
}

/// <summary>
/// Ensures loaded runtime options stay within safe and meaningful ranges.
/// </summary>
static GuardianOptions NormalizeOptions(GuardianOptions options)
{
    options.CpuSpikeMultiplier = Clamp(options.CpuSpikeMultiplier, 1.0, 10.0, 1.5);
    options.DiskUsageWarningPercent = Clamp(options.DiskUsageWarningPercent, 1, 100, 85);
    options.RamUsageWarningPercent = Clamp(options.RamUsageWarningPercent, 1, 100, 85);
    options.ProcessCpuCriticalPercent = Clamp(options.ProcessCpuCriticalPercent, 1, 100, 85);
    options.ProcessMemoryWarningPercent = Clamp(options.ProcessMemoryWarningPercent, 1, 100, 30);
    options.MemoryTrendAlertPercent = Clamp(options.MemoryTrendAlertPercent, 1, 100, 70);
    options.RiskRamPercent = Clamp(options.RiskRamPercent, 1, 100, 80);
    options.AlertCooldownMinutes = Clamp(options.AlertCooldownMinutes, 0, 1440, 30);
    options.MaxOpenPortsWarningCount = ClampInt(options.MaxOpenPortsWarningCount, 1, 1000, 20);
    options.AlertHistoryMaxEntries = ClampInt(options.AlertHistoryMaxEntries, 1, 200000, 5000);
    options.MemoryTrendSamples = ClampInt(options.MemoryTrendSamples, 2, 100, 5);
    options.NotificationMaxAttempts = ClampInt(options.NotificationMaxAttempts, 1, 10, 3);
    options.NotificationBaseDelaySeconds = ClampInt(options.NotificationBaseDelaySeconds, 1, 60, 2);
    options.ScanIntervalSeconds = ClampInt(options.ScanIntervalSeconds, 0, 86400, 0);

    if (string.IsNullOrWhiteSpace(options.MemoryLogFilePath))
    {
        options.MemoryLogFilePath = "/tmp/guardian-memory.log";
    }

    if (string.IsNullOrWhiteSpace(options.AlertStateFilePath))
    {
        options.AlertStateFilePath = "/tmp/guardian-alert-state.json";
    }

    if (string.IsNullOrWhiteSpace(options.AlertHistoryFilePath))
    {
        options.AlertHistoryFilePath = "/tmp/guardian-history.jsonl";
    }

    options.WebhookUrl = options.WebhookUrl?.Trim() ?? string.Empty;
    options.OllamaBaseUrl = options.OllamaBaseUrl?.Trim() ?? "http://localhost:11434";
    options.OllamaModel = options.OllamaModel?.Trim() ?? "llama3.2";

    if (string.IsNullOrWhiteSpace(options.WebhookAuthHeader))
    {
        options.WebhookAuthHeader = "Authorization";
    }
    else
    {
        options.WebhookAuthHeader = options.WebhookAuthHeader.Trim();
    }

    if (string.IsNullOrWhiteSpace(options.OllamaBaseUrl))
    {
        options.OllamaBaseUrl = "http://localhost:11434";
    }

    if (string.IsNullOrWhiteSpace(options.OllamaModel))
    {
        options.OllamaModel = "llama3.2";
    }

    return options;
}

/// <summary>
/// Resolves cycle interval from CLI args and configuration options.
/// </summary>
static TimeSpan ResolveRunInterval(string[] args, GuardianOptions options)
{
    if (args.Any(arg => string.Equals(arg, "--once", StringComparison.OrdinalIgnoreCase)))
    {
        return TimeSpan.Zero;
    }

    if (TryGetArgValue(args, "--interval", out var intervalArg))
    {
        if (!TryParseDuration(intervalArg, out var parsedDuration))
        {
            throw new ArgumentException(
                "Invalid --interval value. Use formats like 30s, 5m, 1h, 1d, or plain seconds.");
        }

        return parsedDuration;
    }

    return options.ScanIntervalSeconds > 0
        ? TimeSpan.FromSeconds(options.ScanIntervalSeconds)
        : TimeSpan.Zero;
}

/// <summary>
/// Reads argument value in the form: --name value.
/// </summary>
static bool TryGetArgValue(IReadOnlyList<string> args, string name, out string value)
{
    for (var i = 0; i < args.Count - 1; i++)
    {
        if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        value = args[i + 1];
        return true;
    }

    value = string.Empty;
    return false;
}

/// <summary>
/// Parses duration strings like 30s, 5m, 1h, 1d, or plain seconds.
/// </summary>
static bool TryParseDuration(string raw, out TimeSpan duration)
{
    return DurationParser.TryParse(raw, out duration);
}

/// <summary>
/// Reads a string configuration value and falls back when missing or blank.
/// </summary>
static string GetString(IConfiguration configuration, string key, string defaultValue)
{
    var value = configuration[key];
    return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
}

/// <summary>
/// Returns the first non-empty string from the provided candidates.
/// </summary>
static string FirstNonEmpty(params string?[] values)
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

/// <summary>
/// Checks whether a URL is absolute and uses HTTP or HTTPS.
/// </summary>
static bool IsValidHttpUrl(string? value)
{
    return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

/// <summary>
/// Reads a double configuration value using invariant culture and falls back on parse failure.
/// </summary>
static double GetDouble(IConfiguration configuration, string key, double defaultValue)
{
    var value = configuration[key];
    return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : defaultValue;
}

/// <summary>
/// Reads an integer configuration value and falls back on parse failure.
/// </summary>
static int GetInt(IConfiguration configuration, string key, int defaultValue)
{
    var value = configuration[key];
    return int.TryParse(value, out var parsed) ? parsed : defaultValue;
}

/// <summary>
/// Reads a boolean configuration value and falls back on parse failure.
/// </summary>
static bool GetBool(IConfiguration configuration, string key, bool defaultValue)
{
    var value = configuration[key];
    return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
}

/// <summary>
/// Constrains a number between min and max, applying a default when the input is invalid.
/// </summary>
static double Clamp(double value, double min, double max, double defaultValue)
{
    if (double.IsNaN(value) || double.IsInfinity(value))
    {
        return defaultValue;
    }

    return Math.Min(max, Math.Max(min, value));
}

/// <summary>
/// Constrains an integer between min and max, applying a default when the input is invalid.
/// </summary>
static int ClampInt(int value, int min, int max, int defaultValue)
{
    if (value < min || value > max)
    {
        return defaultValue;
    }

    return value;
}

/// <summary>
/// Validates a numeric option range and stores a human-readable error when out of range.
/// </summary>
static void ValidateRange(double value, double min, double max, string name, List<string> errors)
{
    if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
    {
        errors.Add($"{name} must be between {min} and {max}.");
    }
}

/// <summary>
/// Validates an integer option range and stores a human-readable error when out of range.
/// </summary>
static void ValidateIntRange(int value, int min, int max, string name, List<string> errors)
{
    if (value < min || value > max)
    {
        errors.Add($"{name} must be between {min} and {max}.");
    }
}
