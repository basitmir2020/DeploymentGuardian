# Deployment Guardian Docs

Deployment Guardian is a .NET 10 monitoring and alerting app for Linux and Windows servers. It collects host health and security signals, evaluates rules, scores risk, and sends actionable alerts.

## What This App Does

- Loads config from `Config/guardian.json` plus `GUARDIAN_` environment overrides.
- Supports single run mode and interval mode (`--once` or `--interval`).
- Detects platform at startup and uses Linux or Windows collectors.
- Collects core runtime metrics:
  - CPU load and CPU cores
  - RAM usage percent
  - Disk usage percent
  - Open ports
  - Running services
  - Top processes by CPU/memory
- Evaluates alert rules for:
  - CPU spike
  - Disk pressure
  - High RAM usage
  - Memory leak trend
  - Firewall disabled
  - SSH hardening issues
  - Fail2Ban inactive
  - Too many open ports
  - Hot processes (CPU/memory)
  - Missing process data
  - Missing running services / SSH service
- Calculates a `0-100` risk score from runtime and security posture.
- Deduplicates alerts with cooldown persistence to prevent alert spam.
- Persists cycle history in JSONL for post-incident analysis.
- Builds detailed alert messages with:
  - Severity counts
  - System snapshot
  - Top process snapshot
  - Per-alert evidence
  - Suggested fix actions
- Optionally appends AI suggestions (`EnableOpenAiSuggestions=true` + `OPENAI_API_KEY`).
- Sends notifications to:
  - Telegram
  - Webhook
  - Or both (fan-out)
- Retries transient notification failures with exponential backoff.
- Uses structured logging with run and cycle scopes.

## Document Map

- [Architecture](./architecture.md)
- [Configuration](./configuration.md)
- [Alerts Reference](./alerts.md)
- [Operations Guide](./operations.md)
- [Development Guide](./development.md)
- [Install Guide](./install.md)

