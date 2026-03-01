using System.Text;
using System.Text.Json;
using DeploymentGuardian.Abstractions;

namespace DeploymentGuardian.Modules;

public class OllamaAdvisor : IAiAdvisor
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly Uri _chatEndpoint;
    private readonly string _model;

    /// <summary>
    /// Creates an Ollama advisor using local endpoint and model name.
    /// </summary>
    public OllamaAdvisor(string baseUrl, string model)
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

        _chatEndpoint = new Uri(parsedBaseUrl, "/api/chat");
        _model = model.Trim();
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

        var payload = new
        {
            model = _model,
            stream = false,
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

        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return ExtractMessageText(json);
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
