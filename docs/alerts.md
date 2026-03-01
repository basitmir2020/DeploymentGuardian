# Alerts Reference

## Severity Levels

- `Info`
  - Informational conditions.
- `Warning`
  - Needs attention soon.
- `Critical`
  - Urgent issue likely impacting stability/security.

## Alert Codes

| Code | Severity | Trigger |
|---|---|---|
| `CPU_SPIKE` | Critical | `CpuLoad > CpuCores * CpuSpikeMultiplier` |
| `DISK_USAGE_HIGH` | Warning | `DiskUsagePercent > DiskUsageWarningPercent` |
| `RAM_USAGE_HIGH` | Warning | `RamUsagePercent > RamUsageWarningPercent` |
| `MEMORY_LEAK_TREND` | Warning | Last `MemoryTrendSamples` values are non-decreasing and latest value exceeds `MemoryTrendAlertPercent` |
| `FIREWALL_DISABLED` | Warning | Firewall reported inactive |
| `ROOT_LOGIN_ENABLED` | Critical | Root SSH login not disabled |
| `SSH_PASSWORD_AUTH_ENABLED` | Warning | SSH password authentication enabled |
| `FAIL2BAN_INACTIVE` | Warning | Fail2Ban not active |
| `OPEN_PORTS_HIGH` | Warning | `OpenPorts.Count > MaxOpenPortsWarningCount` |
| `PROCESS_DATA_MISSING` | Warning | Process collector returned no processes |
| `PROCESS_CPU_HIGH` | Critical | Process CPU `>= ProcessCpuCriticalPercent` (top 3) |
| `PROCESS_MEMORY_HIGH` | Warning | Process memory `>= ProcessMemoryWarningPercent` (top 3) |
| `SERVICES_NONE_RUNNING` | Critical | No running services detected |
| `SSH_SERVICE_MISSING` | Warning | SSH service not found in running service list |
| `GENERIC` | Info | fallback code |

## Risk Score (0-100)

Risk score is additive and capped at `100`:

- `+20` if `RamUsagePercent > RiskRamPercent`
- `+20` if `CpuLoad > CpuCores`
- `+15` if firewall disabled
- `+20` if root login enabled
- `+10` if SSH password auth enabled
- `+5` if Fail2Ban inactive
- `+5` if open ports exceed threshold
- `+5` if any process CPU is above critical threshold

## Dedup And Cooldown

- Dedup key format: `CODE:target:severity`
- Key is persisted in `AlertStateFilePath`.
- Alerts sent within `AlertCooldownMinutes` are suppressed.
- Suppressed alert count is shown in outbound messages.

## Message Composition

Each alert message includes:

- Generated timestamp (UTC)
- Risk score
- Severity summary
- Cooldown suppression summary
- System snapshot
- Top process snapshot
- Alert details with evidence
- Suggested fix actions
- Optional AI suggestions

## Known Alerting Notes

- `ServiceRules` SSH detection uses Linux-style names (`ssh.service`, `sshd.service`).
- Windows deployments may require adapting service-name checks for `SSH_SERVICE_MISSING`.

