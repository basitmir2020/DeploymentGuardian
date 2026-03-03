# Development Guide

## Prerequisites

- .NET SDK 10.0+
- Linux or Windows host
- Linux or Windows host
- Optional:
  - Telegram bot credentials for alert delivery tests
  - Webhook endpoint for integration tests
  - An AI provider setup (Ollama + `qwen2.5:0.5b`, an OpenAI API key, or a running llama.cpp server) for testing the local AI pipeline

## Build And Test

Build:

```bash
dotnet build
```

Run tests:

```bash
dotnet test DeploymentGuardian.Tests/DeploymentGuardian.Tests.csproj
```

## Performance Profiling

Install tools once:

```bash
dotnet tool install --global dotnet-counters
dotnet tool install --global dotnet-trace
```

Capture runtime counters on interval mode:

```bash
dotnet-counters collect --duration 00:00:20 --refresh-interval 1 --format csv --output profiler-counters-interval.csv --counters System.Runtime -- .\bin\Debug\net10.0\DeploymentGuardian.exe --interval 1s
```

Capture CPU sampling trace:

```bash
dotnet-trace collect --duration 00:00:20 --output profiler-interval.nettrace --providers Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x4c14fccbd:5 -- .\bin\Debug\net10.0\DeploymentGuardian.exe --interval 1s
dotnet-trace report profiler-interval.nettrace topN --inclusive -n 20
```

Profiler artifacts should be checked into neither git nor release bundles.

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

## AI Advisor Extension Pattern

- Implement `IAiAdvisor` in `Abstractions/`. This interface now requires implementations for all 5 alert phases: `GetSuggestionsAsync`, `GetImplementationStepsAsync`, `GetSecuritySuggestionsAsync`, `GetPerformanceTuningAsync`, and `GetSuggestionsStreamAsync`.
- Define an instantiation strategy within the `BuildAiAdvisor(...)` factory method in `Program.cs`.
- Endpoint and API key logic should use dedicated environment variables depending on the service (e.g. `OLLAMA_BASE_URL`, `OPENAI_API_KEY`).

## Coding Expectations

- Keep behavior resilient: collectors should fail soft, not crash full cycle.
- Keep option validation strict and explicit.
- Preserve backward compatibility for alert codes when possible.
- Add tests for:
  - parser logic
  - dedup behavior
  - notifier behavior
  - AI suggestion background handling and schema parsing
  - any new rule threshold logic
