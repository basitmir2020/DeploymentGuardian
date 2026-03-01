using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeploymentGuardian.Abstractions;

namespace DeploymentGuardian.Services;

public class WebhookNotifier : INotifier
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly Uri _endpoint;
    private readonly string? _authHeaderName;
    private readonly string? _authHeaderValue;

    /// <summary>
    /// Creates a webhook notifier that posts alert payloads as JSON.
    /// </summary>
    public WebhookNotifier(string endpoint, string? authHeaderName, string? authHeaderValue)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Webhook endpoint is required.", nameof(endpoint));
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var parsedEndpoint) ||
            (parsedEndpoint.Scheme != Uri.UriSchemeHttp && parsedEndpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Webhook endpoint must be an absolute HTTP/HTTPS URL.", nameof(endpoint));
        }

        _endpoint = parsedEndpoint;
        _authHeaderName = string.IsNullOrWhiteSpace(authHeaderName) ? null : authHeaderName.Trim();
        _authHeaderValue = string.IsNullOrWhiteSpace(authHeaderValue) ? null : authHeaderValue.Trim();
    }

    /// <summary>
    /// Sends alert text to a webhook endpoint in JSON format.
    /// </summary>
    public async Task SendAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var payload = new
        {
            source = "DeploymentGuardian",
            sentAtUtc = DateTimeOffset.UtcNow,
            message
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_authHeaderName) && !string.IsNullOrWhiteSpace(_authHeaderValue))
        {
            if (string.Equals(_authHeaderName, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(_authHeaderValue);
            }
            else
            {
                request.Headers.TryAddWithoutValidation(_authHeaderName, _authHeaderValue);
            }
        }

        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
