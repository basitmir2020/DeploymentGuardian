using System.Net.Http.Headers;
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

        var payload = new
        {
            model = _model,
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
            if (!first.TryGetProperty("message", out var message))
            {
                return string.Empty;
            }

            if (!message.TryGetProperty("content", out var contentElement))
            {
                return string.Empty;
            }

            return contentElement.GetString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
