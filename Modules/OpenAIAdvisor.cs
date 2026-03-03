using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DeploymentGuardian.Abstractions;

namespace DeploymentGuardian.Modules;

public class OpenAiAdvisor : IAiAdvisor
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly string? _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    /// <summary>
    /// Sends a summary to OpenAI and returns extracted assistant guidance text.
    /// </summary>
    public async Task<string> GetSuggestionsAsync(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Summary must not be empty.", nameof(summary));
        }

        var payload = BuildStandardPayload(
            "You are a DevOps advisor. Give concise, actionable mitigation steps.",
            summary);

        return await ExecutePromptAsync(payload);
    }

    public async Task<string> GetImplementationStepsAsync(string suggestions)
    {
        if (string.IsNullOrWhiteSpace(suggestions)) return string.Empty;

        var payload = BuildStandardPayload(
            "You are a DevOps engineer. Based on these suggestions, provide the EXACT shell commands needed to implement them. NO markdown blocks or explanations, just the raw commands.",
            suggestions);

        return await ExecutePromptAsync(payload);
    }

    public async Task<string> GetSecuritySuggestionsAsync(string securitySummary)
    {
        if (string.IsNullOrWhiteSpace(securitySummary)) return string.Empty;

        var payload = BuildStandardPayload(
            "You are a Cloud Security Consultant. Analyze this server security state and provide specific, actionable hardening advice.",
            securitySummary);

        return await ExecutePromptAsync(payload);
    }

    public async Task<string> GetPerformanceTuningAsync(string metricsSummary)
    {
        if (string.IsNullOrWhiteSpace(metricsSummary)) return string.Empty;

        var payload = BuildStandardPayload(
            "You are a Systems Architect. Review these server hardware metrics and explain how to configure this machine to its absolute maximum potential without risking a crash (e.g., precise swap sizing, connection limits, etc).",
            metricsSummary);

        return await ExecutePromptAsync(payload);
    }

    private object BuildStandardPayload(string systemPrompt, string userPrompt, bool stream = false)
    {
        return new
        {
            model = "gpt-4o-mini",
            stream = stream,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };
    }

    private async Task<string> ExecutePromptAsync(object payload)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY is not configured.");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync();
        return ExtractMessageText(result);
    }

    public async IAsyncEnumerable<string> GetSuggestionsStreamAsync(string summary, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(summary)) yield break;
        var payload = BuildStandardPayload("You are a DevOps advisor. Give concise, actionable mitigation steps.", summary, true);
        await foreach (var chunk in ExecuteStreamPromptAsync(payload, cancellationToken)) yield return chunk;
    }

    public async IAsyncEnumerable<string> GetImplementationStepsStreamAsync(string suggestions, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(suggestions)) yield break;
        var payload = BuildStandardPayload("You are a DevOps engineer. Based on these suggestions, provide the EXACT shell commands needed to implement them. NO markdown blocks or explanations, just the raw commands.", suggestions, true);
        await foreach (var chunk in ExecuteStreamPromptAsync(payload, cancellationToken)) yield return chunk;
    }

    public async IAsyncEnumerable<string> GetSecuritySuggestionsStreamAsync(string securitySummary, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(securitySummary)) yield break;
        var payload = BuildStandardPayload("You are a Cloud Security Consultant. Analyze this server security state and provide specific, actionable hardening advice.", securitySummary, true);
        await foreach (var chunk in ExecuteStreamPromptAsync(payload, cancellationToken)) yield return chunk;
    }

    public async IAsyncEnumerable<string> GetPerformanceTuningStreamAsync(string metricsSummary, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metricsSummary)) yield break;
        var payload = BuildStandardPayload("You are a Systems Architect. Review these server hardware metrics and explain how to configure this machine to its absolute maximum potential without risking a crash (e.g., precise swap sizing, connection limits, etc).", metricsSummary, true);
        await foreach (var chunk in ExecuteStreamPromptAsync(payload, cancellationToken)) yield return chunk;
    }

    private async IAsyncEnumerable<string> ExecuteStreamPromptAsync(object payload, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) throw new InvalidOperationException("OPENAI_API_KEY is not configured.");

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                var data = line.Substring(6).Trim();
                if (data == "[DONE]") break;

                var chunk = ExtractMessageText(data);
                if (!string.IsNullOrWhiteSpace(chunk)) yield return chunk;
            }
        }
    }

    /// <summary>
    /// Parses chat-completions JSON and extracts first assistant message text.
    /// </summary>
    private static string ExtractMessageText(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                return string.Empty;
            }

            var first = choices[0];
            
            // Handle both streaming (delta) and non-streaming (message) formats
            if (first.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var contentElement))
                {
                    return contentElement.GetString() ?? string.Empty;
                }
            }
            else if (first.TryGetProperty("message", out var message))
            {
                if (message.TryGetProperty("content", out var contentElement))
                {
                    return contentElement.GetString() ?? string.Empty;
                }
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
