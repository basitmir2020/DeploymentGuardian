using System.Net.Http.Headers;
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

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY is not configured.");
        }

        var payload = new
        {
            model = "gpt-4o-mini",
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are a DevOps advisor. Give concise, actionable mitigation steps."
                },
                new { role = "user", content = summary }
            }
        };

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
