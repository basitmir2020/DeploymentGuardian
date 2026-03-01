# Configuration

## Sources And Precedence

Configuration is loaded in this order:

1. `Config/guardian.json`
2. Environment variables with `GUARDIAN_` prefix (override file)

Example:

- `GUARDIAN_RamUsageWarningPercent=90`
- `GUARDIAN_ScanIntervalSeconds=300`

Notification credentials use dedicated environment variables (not `GUARDIAN_`):

- `TELEGRAM_TOKEN`
- `TELEGRAM_CHAT_ID`
- `WEBHOOK_URL`
- `WEBHOOK_AUTH_HEADER`
- `WEBHOOK_AUTH_VALUE`
- `OPENAI_API_KEY`

AI toggles and model/base-url settings still use `GUARDIAN_` prefix, for example:

- `GUARDIAN_EnableOpenAiSuggestions=true`
- `GUARDIAN_EnableOllamaSuggestions=true`
- `GUARDIAN_OllamaBaseUrl=http://localhost:11434`
- `GUARDIAN_OllamaModel=llama3.2`

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
| `EnableOllamaSuggestions` | `false` | cannot be true with `EnableOpenAiSuggestions` | Enables local Ollama AI suggestions |
| `OllamaBaseUrl` | `http://localhost:11434` | absolute `http/https` when Ollama enabled | Ollama server URL |
| `OllamaModel` | `llama3.2` | required when Ollama enabled | Ollama model name |
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
| `EnableOpenAiSuggestions` | `false` | requires `OPENAI_API_KEY` when true | Append AI remediation notes |

## Notification Configuration

### Telegram

Required:

- `TELEGRAM_TOKEN`
- `TELEGRAM_CHAT_ID`

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

## AI Provider Behavior

- `EnableOpenAiSuggestions=true` uses `OpenAiAdvisor`.
- `EnableOllamaSuggestions=true` uses `OllamaAdvisor`.
- You cannot enable both providers at the same time.
- OpenAI requires `OPENAI_API_KEY`.
- Ollama requires:
  - `OllamaBaseUrl` (for example `http://localhost:11434`)
  - `OllamaModel` (for example `llama3.2`)

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
  "EnableOllamaSuggestions": false,
  "OllamaBaseUrl": "http://localhost:11434",
  "OllamaModel": "llama3.2",
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
  "ScanIntervalSeconds": 0,
  "EnableOpenAiSuggestions": false
}
```
