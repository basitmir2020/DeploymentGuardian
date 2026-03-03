using DeploymentGuardian.Abstractions;

namespace DeploymentGuardian.Services;

public class TelegramNotifier : INotifier
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly string _token;
    private readonly string _chatId;

    /// <summary>
    /// Creates a Telegram notifier from bot token and chat id.
    /// </summary>
    public TelegramNotifier(string token, string chatId)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Telegram token is required.", nameof(token));
        }

        if (string.IsNullOrWhiteSpace(chatId))
        {
            throw new ArgumentException("Telegram chat id is required.", nameof(chatId));
        }

        _token = token;
        _chatId = chatId;
    }

    /// <summary>
    /// Sends alert text to the configured Telegram chat.
    /// </summary>
    public async Task SendAsync(string message)
    {
        await SendTrackedAsync(message);
    }

    public async Task<string?> SendTrackedAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var response = await HttpClient.PostAsync(
            $"https://api.telegram.org/bot{_token}/sendMessage",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("chat_id", _chatId),
                new KeyValuePair<string, string>("text", message)
            ]));

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(content);
        if (doc.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("message_id", out var msgId))
        {
            return msgId.GetInt64().ToString();
        }

        return null;
    }

    public async Task EditTrackedAsync(string trackingId, string message)
    {
        if (string.IsNullOrWhiteSpace(trackingId) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var response = await HttpClient.PostAsync(
            $"https://api.telegram.org/bot{_token}/editMessageText",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("chat_id", _chatId),
                new KeyValuePair<string, string>("message_id", trackingId),
                new KeyValuePair<string, string>("text", message)
            ]));

        response.EnsureSuccessStatusCode();
    }
}
