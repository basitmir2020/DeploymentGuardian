# Architecture

## High-Level Flow

1. App loads configuration from `Config/guardian.json` and `GUARDIAN_*` environment variables.
2. Runtime options are validated and normalized.
3. Platform-specific collectors are selected:
   - Linux: shell-based analyzers
   - Windows: native/process-based collectors
4. A monitoring cycle gathers server context.
5. Rule engine produces alerts.
6. Memory trend detector can add a memory leak trend alert.
7. Risk score is calculated.
8. Alerts are cooldown-filtered (dedup).
9. Detailed message is generated (human-readable UTC timestamp, evidence, fix actions).
10. AI guidance is appended from local Ollama.
11. Message is sent through configured notifier(s).
12. Delivery state and history are persisted.

## Runtime Components

- `Program.cs`
  - App bootstrap, lifecycle, option loading/validation, cycle loop.
- `Abstractions/`
  - Contracts for collectors, rules, analyzers, notifier.
- `Modules/`
  - Data collection, rules engine, risk calculation, memory trend, dedup, alert history, AI advisor.
- `Rules/`
  - Alert policy classes evaluated each cycle.
- `Services/`
  - Notification transports and wrappers (`Telegram`, `Webhook`, `Retrying`, `MultiNotifier`).
  - Telegram inbound setup assistant (`TelegramSetupAssistant`) for stack-driven readiness checks and gated setup execution.
- `Models/`
  - Data models for context, alerts, reports, options, persistence.
- `Utils/`
  - Duration parsing and shell command execution helpers.

## Platform Collection Model

### Linux path

- `ServerAnalyzer`: `cat /proc/loadavg`, `nproc`, `free -m`, `df -h /`, `swapon --show`
- `ProcessAnalyzer`: `ps -eo comm,%cpu,%mem --sort=-%cpu | head -6`
- `SecurityScanner`: checks `ufw`, `sshd_config`, `fail2ban`, listening ports (`ss`)
- `ServiceScanner`: running `systemd` services

### Windows path

- `WindowsServerCollector`: process-time sampled CPU, physical memory, disk usage, pagefile presence
- `WindowsProcessCollector`: sampled per-process CPU and memory share
- `WindowsSecurityCollector`: firewall via `netsh`, open ports via `IPGlobalProperties`
- `WindowsServiceCollector`: running services via `sc query`

## Alert Pipeline

- Input: `ServerContext`
- Processing:
  - `RequirementEngine` runs all rules.
  - `MemoryTrendAnalyzer` optionally adds `MEMORY_LEAK_TREND`.
  - `AlertDeduplicator` filters by cooldown using stable keys (`code:target:severity`).
- Output:
  - `DeploymentReport` with `Alerts` and `RiskScore`.
  - Detailed outbound message payload.
  - Timestamp format in alert payload:
    - `Generated At (UTC): dd/MMM/yyyy : hh:mm:ss tt`

## AI Advisor Pipeline

- `IAiAdvisor` is the shared abstraction used by alert message enrichment.
- `BuildAiAdvisor(...)` in `Program.cs` always builds `OllamaAdvisor`.
- Model is fixed to `qwen2.5:0.5b`.
- Runtime endpoint and timeout come from env vars:
  - `OLLAMA_BASE_URL`
  - `OLLAMA_TIMEOUT_SECONDS`

## Notification Pipeline

- Channels are built from runtime configuration:
  - Telegram if `TELEGRAM_TOKEN` + `TELEGRAM_CHAT_ID` are present.
  - Webhook if `WEBHOOK_URL` or `WebhookUrl` is set.
- Each channel is wrapped in `RetryingNotifier`.
- If multiple channels are configured, `MultiNotifier` fan-outs to all.
- If any channel fails in fan-out, send is considered failed for that cycle.

## Telegram Setup Assistant Pipeline

- Polls Telegram Bot API `getUpdates` in interval mode.
- Accepts stack messages (Angular/React/Next.js/Blazor/ASP.NET Core/Vue/Node.js).
- Builds server prerequisite status using platform-aware command probes.
- Returns readiness report and waits for explicit `okay`.
- Executes predefined setup commands only after confirmation.
- Supports `steps` (show commands) and `cancel` (discard plan).

## Persistence Files

- Memory trend samples:
  - `MemoryLogFilePath` (default: `/tmp/guardian-memory.log`)
- Dedup state:
  - `AlertStateFilePath` (default: `/tmp/guardian-alert-state.json`)
- Alert history:
  - `AlertHistoryFilePath` (default: `/tmp/guardian-history.jsonl`)

## Logging

- Console logging with UTC timestamp.
- Includes scoped fields:
  - `runId` for process lifetime
  - `cycle` for each monitoring iteration

## Current Design Notes

- Service SSH detection in `ServiceRules` is Linux-oriented (`ssh.service` / `sshd.service`).
- Default file paths are Linux-style (`/tmp/...`), so Windows deployments should override them.
- `AppAnalyzer` currently returns a static app profile and can be replaced with real project inspection later.
