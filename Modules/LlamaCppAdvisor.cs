using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DeploymentGuardian.Abstractions;

namespace DeploymentGuardian.Modules;

public class LlamaCppAdvisor : IAiAdvisor
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly Uri _chatEndpoint;
    private readonly string _model;
    private readonly string? _apiKey;

    /// <summary>
    /// Creates a llama.cpp advisor using OpenAI-compatible chat endpoint.
    /// </summary>
    public LlamaCppAdvisor(string baseUrl, string model, string? apiKey = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("llama.cpp base URL is required.", nameof(baseUrl));
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl) ||
            (parsedBaseUrl.Scheme != Uri.UriSchemeHttp && parsedBaseUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("llama.cpp base URL must be an absolute HTTP/HTTPS URL.", nameof(baseUrl));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("llama.cpp model name is required.", nameof(model));
        }

        _chatEndpoint = new Uri(parsedBaseUrl, "/v1/chat/completions");
        _model = model.Trim();
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    /// <summary>
    /// Sends a summary to llama.cpp and returns extracted assistant guidance text.
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

    private object BuildStandardPayload(string systemPrompt, string userPrompt)
    {
        return new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };
    }

    private async Task<string> ExecutePromptAsync(object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return ExtractMessageText(json);
    }

    public async IAsyncEnumerable<string> GetSuggestionsStreamAsync(string summary, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            yield break;
        }

        var payload = new
        {
            model = _model,
            stream = true,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are a DevOps advisor. Give concise, actionable mitigation steps."
                },
                new
                {
                    role = "user",
                    content = summary
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("data: "))
            {
                var data = line.Substring(6).Trim();
                if (data == "[DONE]")
                {
                    break;
                }

                var chunk = ExtractMessageText(data);
                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    yield return chunk;
                }
            }
        }
    }

    /// <summary>
    /// Parses OpenAI-compatible JSON and extracts first assistant message text.
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
