# Configuration

## Sources And Precedence

Configuration is loaded in this order:

1. `Config/guardian.json`
2. Environment variables with `GUARDIAN_` prefix (override file)

Example:

- `GUARDIAN_RamUsageWarningPercent=90`
- `GUARDIAN_ScanIntervalSeconds=300`

Dedicated environment variables (not `GUARDIAN_`) are used for notifications and Ollama:

- `TELEGRAM_TOKEN`
- `TELEGRAM_CHAT_ID`
- `TELEGRAM_SETUP_ASSISTANT_ENABLED` (optional, default `true`)
- `TELEGRAM_SETUP_ASSISTANT_POLL_SECONDS` (optional, default `5`, valid `1..60`)
- `TELEGRAM_SETUP_ASSISTANT_USE_SUDO` (optional, default `false`)
- `TELEGRAM_SETUP_ASSISTANT_STOP_ON_REQUIRED_FAILURE` (optional, default `true`)
- `WEBHOOK_URL`
- `WEBHOOK_AUTH_HEADER`
- `WEBHOOK_AUTH_VALUE`
- `WEBHOOK_AUTH_VALUE`
- `AI_PROVIDER` (optional, default `none`, valid options `ollama`, `openai`, `llamacpp`, `none`)
- `AI_MODEL` (optional, default `qwen2.5:0.5b` for ollama/llamacpp, `gpt-4o-mini` for openai)
- `OLLAMA_BASE_URL` (optional, default `http://127.0.0.1:11434`)
- `OLLAMA_TIMEOUT_SECONDS` (optional, default `120`, valid `5..600`)
- `LLAMACPP_BASE_URL` (optional, default `http://127.0.0.1:8080`)
- `OPENAI_API_KEY` (required if provider is `openai`)

## CLI Arguments

- `--once`
  - Run one cycle and exit.
- `--interval <duration>`
  - Run repeatedly with duration like `30s`, `5m`, `1h`, `1d`, or plain seconds.

If no CLI interval is provided, `ScanIntervalSeconds` is used.

## App Settings

| Key | Default | Valid Range / Rules | Purpose |
|---|---|---|---|
| `MemoryLogFilePath` | `/tmp/guardian-memory.log` | required | Memory trend sample log |
| `AlertStateFilePath` | `/tmp/guardian-alert-state.json` | required | Dedup cooldown state |
| `AlertHistoryFilePath` | `/tmp/guardian-history.jsonl` | required | Alert cycle history JSONL |
| `AlertHistoryMaxEntries` | `5000` | `1..200000` | Max retained history entries |
| `WebhookUrl` | `""` | absolute `http/https` when set | Optional webhook endpoint |
| `WebhookAuthHeader` | `Authorization` | optional | Header name for webhook auth |
| `CpuSpikeMultiplier` | `1.5` | `1..10` | CPU load vs cores critical multiplier |
| `DiskUsageWarningPercent` | `85` | `1..100` | Disk warning threshold |
| `RamUsageWarningPercent` | `85` | `1..100` | RAM warning threshold |
| `ProcessCpuCriticalPercent` | `85` | `1..100` | Per-process CPU critical threshold |
| `ProcessMemoryWarningPercent` | `30` | `1..100` | Per-process memory warning threshold |
| `MaxOpenPortsWarningCount` | `20` | `1..1000` | Open ports warning threshold |
| `MemoryTrendAlertPercent` | `70` | `1..100` | Memory trend alert floor |
| `MemoryTrendSamples` | `5` | `2..100` | Samples required for monotonic trend |
| `RiskRamPercent` | `80` | `1..100` | RAM threshold used in risk score |
| `AlertCooldownMinutes` | `30` | `0..1440` | Dedup cooldown window |
| `NotificationMaxAttempts` | `3` | `1..10` | Retry attempts per channel |
| `NotificationBaseDelaySeconds` | `2` | `1..60` | Base exponential backoff delay |
| `ScanIntervalSeconds` | `0` | `0..86400` | `0` means single-run mode |

## Notification Configuration

### Telegram

Required:

- `TELEGRAM_TOKEN`
- `TELEGRAM_CHAT_ID`

Optional setup assistant controls:

- `TELEGRAM_SETUP_ASSISTANT_ENABLED=true|false`
- `TELEGRAM_SETUP_ASSISTANT_POLL_SECONDS=5`
- `TELEGRAM_SETUP_ASSISTANT_USE_SUDO=true|false`
- `TELEGRAM_SETUP_ASSISTANT_STOP_ON_REQUIRED_FAILURE=true|false`

### Webhook

Required:

- `WEBHOOK_URL` (or `WebhookUrl` in config)

Optional:

- `WEBHOOK_AUTH_HEADER` (default from config `WebhookAuthHeader`)
- `WEBHOOK_AUTH_VALUE`

Webhook payload shape:

```json
{
  "source": "DeploymentGuardian",
  "sentAtUtc": "2026-03-01T20:00:00.0000000+00:00",
  "message": "Deployment Guardian Alert ..."
}
```

### Multi-channel behavior

- If both Telegram and webhook are configured, both are used.
- Retry policy applies per channel.
- If one channel fails, cycle delivery is marked failed.

## Telegram Setup Assistant

- Runs only when:
  - `TELEGRAM_TOKEN` + `TELEGRAM_CHAT_ID` are configured
  - `ScanIntervalSeconds > 0` (interval mode)
  - `TELEGRAM_SETUP_ASSISTANT_ENABLED` is not set to `false`
- It accepts free-form technology messages (for example `My app uses Laravel + Redis`).
- It analyzes current server state and asks AI to generate a setup plan (JSON commands + prerequisites).
- If you reply `okay`, it executes AI-generated commands.
- If `TELEGRAM_SETUP_ASSISTANT_USE_SUDO=true`, Linux install commands are executed through `sudo`.
- If `TELEGRAM_SETUP_ASSISTANT_STOP_ON_REQUIRED_FAILURE=true`, setup halts when a required step fails.
- `steps` prints commands without executing.
- `cancel` discards the pending setup plan.

## AI Behavior

- AI provider can be configured via `AI_PROVIDER` environment variable (`ollama`, `openai`, `llamacpp`, default `none`).
- AI model can be fixed using `AI_MODEL`.
- Once configured, AI alerts run in a decoupled 5-phase background pipeline:
  1. Instant Health Alert
  2. Diagnostics & Suggestions
  3. Actionable Implementation Steps
  4. Server-Specific Security Audit
  5. Hardware Performance Tuning
- Runtime values are read from dedicated env vars depending on the provider:
  - `OLLAMA_BASE_URL` (default `http://127.0.0.1:11434`)
  - `OLLAMA_TIMEOUT_SECONDS` (default `120`)
  - `LLAMACPP_BASE_URL` (default `http://127.0.0.1:8080`)
  - `OPENAI_API_KEY`

## Alert Timestamp Format

Alert messages print timestamp in UTC using:

- `dd/MMM/yyyy : hh:mm:ss tt`
- Example: `23/May/2026 : 07:40:11 PM`

## Example `guardian.json`

```json
{
  "MemoryLogFilePath": "/tmp/guardian-memory.log",
  "AlertStateFilePath": "/tmp/guardian-alert-state.json",
  "AlertHistoryFilePath": "/tmp/guardian-history.jsonl",
  "AlertHistoryMaxEntries": 5000,
  "WebhookUrl": "",
  "WebhookAuthHeader": "Authorization",
  "CpuSpikeMultiplier": 1.5,
  "DiskUsageWarningPercent": 85,
  "RamUsageWarningPercent": 85,
  "ProcessCpuCriticalPercent": 85,
  "ProcessMemoryWarningPercent": 30,
  "MaxOpenPortsWarningCount": 20,
  "MemoryTrendAlertPercent": 70,
  "MemoryTrendSamples": 5,
  "RiskRamPercent": 80,
  "AlertCooldownMinutes": 30,
  "NotificationMaxAttempts": 3,
  "NotificationBaseDelaySeconds": 2,
  "ScanIntervalSeconds": 0
}
```
