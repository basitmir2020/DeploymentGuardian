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
10. Message is instantly sent through configured notifier(s).
11. If AI is configured, a fire-and-forget background sequence starts: requests multi-phase Diagnostics, Implementation Steps, Security Analysis, and Performance Tuning individually from the LLM, and sends out follow-up messages on completion.
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
- Provides support for 5 distinct phases: Suggestions, Implementation Steps, Security Advice, and Performance Tuning.
- Implementations exist for `OllamaAdvisor`, `OpenAIAdvisor`, and `LlamaCppAdvisor`.
- Factory pattern in `Program.cs` (`BuildAiAdvisor`) selects the appropriate provider using the `AI_PROVIDER` environment variable.
- Configuration comes from associated env vars (`AI_MODEL`, `OLLAMA_BASE_URL`, `OPENAI_API_KEY`, etc).

## Notification Pipeline

- Channels are built from runtime configuration:
  - Telegram if `TELEGRAM_TOKEN` + `TELEGRAM_CHAT_ID` are present.
  - Webhook if `WEBHOOK_URL` or `WebhookUrl` is set.
- Each channel is wrapped in `RetryingNotifier`.
- If multiple channels are configured, `MultiNotifier` fan-outs to all.
- If any channel fails in fan-out, send is considered failed for that cycle.

## Telegram Setup Assistant Pipeline

- Polls Telegram Bot API `getUpdates` in interval mode.
- Accepts free-form technology messages (not limited to predefined stacks).
- Builds a server snapshot using platform-aware probes.
- Sends request + snapshot to AI advisor and expects structured JSON plan.
- Returns AI-generated readiness report and waits for explicit `okay`.
- Executes AI-generated setup commands only after confirmation.
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
