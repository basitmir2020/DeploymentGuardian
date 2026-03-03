# Operations Guide

## Common Run Commands

Run one cycle:

```bash
dotnet run -- --once
```

Run continuously every 5 minutes:

```bash
dotnet run -- --interval 5m
```

Use config interval:

```bash
dotnet run
```

## Deployment

Use the Linux server installation guide:

- [install.md](./install.md)

## Runtime Artifacts

- Memory trend log: `MemoryLogFilePath`
- Dedup state file: `AlertStateFilePath`
- Alert history file: `AlertHistoryFilePath`

Alert history is JSONL, one cycle per line.

## Observability

- Console logs use UTC timestamps.
- Log scopes include:
  - `runId` for full process lifetime
  - `cycle` for each monitoring cycle
- Alert messages use readable UTC time:
  - `dd/MMM/yyyy : hh:mm:ss tt`
- Typical healthy cycles log either:
  - `No alerts.`
  - or sent alert count with risk score.

### Runtime Profiling (On Demand)

Capture 20 seconds of runtime counters:

```bash
dotnet-counters collect --duration 00:00:20 --refresh-interval 1 --format csv --output profiler-counters-interval.csv --counters System.Runtime -- .\bin\Debug\net10.0\DeploymentGuardian.exe --interval 1s
```

Capture 20 seconds of sample-based trace:

```bash
dotnet-trace collect --duration 00:00:20 --output profiler-interval.nettrace --providers Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x4c14fccbd:5 -- .\bin\Debug\net10.0\DeploymentGuardian.exe --interval 1s
dotnet-trace report profiler-interval.nettrace topN -n 20
```

## Troubleshooting

### No notifications are sent

Check:

- Telegram:
  - `TELEGRAM_TOKEN`
  - `TELEGRAM_CHAT_ID`
- Webhook:
  - `WEBHOOK_URL`
  - optional `WEBHOOK_AUTH_HEADER`/`WEBHOOK_AUTH_VALUE`

If no channels are configured, app logs:

- `Alerts detected but no notification channel is configured.`

### AI suggestions are not shown

Check:

- Ollama service reachable at `OLLAMA_BASE_URL` (default `http://127.0.0.1:11434`)
- model exists: `ollama pull qwen2.5:0.5b`
- timeout is high enough on slower CPUs:
  - `OLLAMA_TIMEOUT_SECONDS=120` or `180`

### App exits immediately

- Expected in single-run mode:
  - `--once`
  - or `ScanIntervalSeconds = 0` with no `--interval`.

### Config validation fails at startup

- App fails fast with a list of invalid keys/ranges.
- Fix values in `guardian.json`, `GUARDIAN_*`, or Ollama env vars.

### Repeated identical alerts are missing

- Likely suppressed by cooldown.
- Review `AlertCooldownMinutes` and dedup state file.

### Memory leak trend alert appears unexpectedly

- Trigger requires monotonic growth across recent samples.
- Check recent values in `MemoryLogFilePath`.
- Tune:
  - `MemoryTrendSamples`
  - `MemoryTrendAlertPercent`

### Windows path issues with `/tmp` defaults

- Override file paths in config for Windows-friendly locations.

### High Win32Exception rate on Windows

- Process metrics sampling may encounter permission-restricted processes.
- Current collector mitigates churn by:
  - sharing one process-sampling window across Windows collectors
  - caching process windows for short intervals
  - temporarily skipping recently inaccessible PIDs
- If rate is still high in your environment, increase `ScanIntervalSeconds`.

## Safe Maintenance

- Backup/rotate history and memory logs in long-running environments.
- Keep notification secrets in protected env files.
- Validate changes using `--once` before enabling interval mode.
