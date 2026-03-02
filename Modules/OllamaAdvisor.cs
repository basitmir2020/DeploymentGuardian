using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DeploymentGuardian.Abstractions;

namespace DeploymentGuardian.Modules;

public class OllamaAdvisor : IAiAdvisor
{
    private readonly HttpClient _httpClient;
    private readonly Uri _chatEndpoint;
    private readonly string _model;

    /// <summary>
    /// Creates an Ollama advisor using local endpoint and model name.
    /// </summary>
    public OllamaAdvisor(string baseUrl, string model, int timeoutSeconds = 120)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Ollama base URL is required.", nameof(baseUrl));
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl) ||
            (parsedBaseUrl.Scheme != Uri.UriSchemeHttp && parsedBaseUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Ollama base URL must be an absolute HTTP/HTTPS URL.", nameof(baseUrl));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Ollama model is required.", nameof(model));
        }

        if (timeoutSeconds < 5 || timeoutSeconds > 600)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Ollama timeout must be between 5 and 600 seconds.");
        }

        _chatEndpoint = new Uri(parsedBaseUrl, "/api/chat");
        _model = model.Trim();
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
    }

    /// <summary>
    /// Sends a summary to Ollama and returns extracted assistant guidance text.
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
            stream = false,
            options = new { num_predict = 1000 },
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
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(request);
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

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

            var chunk = ExtractMessageText(line);
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                yield return chunk;
            }
        }
    }


    /// <summary>
    /// Parses Ollama JSON response and extracts assistant message text.
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

            if (root.TryGetProperty("message", out var messageElement) &&
                messageElement.TryGetProperty("content", out var contentElement))
            {
                return contentElement.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("response", out var responseElement))
            {
                return responseElement.GetString() ?? string.Empty;
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
