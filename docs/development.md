# Development Guide

## Prerequisites

- .NET SDK 10.0+
- Linux or Windows host
- Optional:
  - Telegram bot credentials for alert delivery tests
  - Webhook endpoint for integration tests
  - `OPENAI_API_KEY` for AI suggestion path
  - Ollama server + pulled model for local AI suggestion path

## Build And Test

Build:

```bash
dotnet build
```

Run tests:

```bash
dotnet test DeploymentGuardian.Tests/DeploymentGuardian.Tests.csproj
```

## Project Structure

- `Program.cs`
  - Entrypoint, orchestration, option validation, message formatting.
- `Abstractions/`
  - Interfaces for pluggable components.
- `Models/`
  - DTOs and state models.
- `Modules/`
  - Collectors, analyzers, engine, dedup/history, risk.
- `Rules/`
  - Policy checks that emit alerts.
- `Services/`
  - Notification channel implementations and wrappers.
- `Utils/`
  - Shell helper and duration parser.
- `DeploymentGuardian.Tests/`
  - Unit tests.

## Adding A New Rule

1. Create a rule class in `Rules/` implementing `IRule<ServerContext>`.
2. Add new alert code constant in `Models/Alert.cs` if needed.
3. Register rule in `Program.cs` rule list.
4. Add evidence and fix-action mapping in:
   - `GetAlertEvidence(...)`
   - `GetFixAction(...)`
5. Add unit tests in `DeploymentGuardian.Tests/`.

## Adding A New Collector

1. Implement one of:
   - `IServerDataCollector`
   - `IProcessDataCollector`
   - `ISecurityDataCollector`
   - `IServiceDataCollector`
2. Wire implementation in platform selection block in `Program.cs`.
3. Ensure safe defaults on parsing/command failures.
4. Add tests for parsing and edge cases where possible.

## Notification Extension Pattern

- Create `INotifier` implementation in `Services/`.
- Wrap it with `RetryingNotifier` in `BuildNotifier`.
- Add to multi-channel list so it can fan-out with existing channels.

## Coding Expectations

- Keep behavior resilient: collectors should fail soft, not crash full cycle.
- Keep option validation strict and explicit.
- Preserve backward compatibility for alert codes when possible.
- Add tests for:
  - parser logic
  - dedup behavior
  - notifier behavior
  - any new rule threshold logic
