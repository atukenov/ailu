using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Ailu.Api.Infrastructure;

public sealed record PushMessage(string To, string Title, string Body);

public interface IPushSender
{
    Task SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken ct = default);
}

// Until push credentials are set up, pushes go to the log. Never enable this in production.
public sealed class LoggingPushSender(ILogger<LoggingPushSender> log) : IPushSender
{
    public Task SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken ct = default)
    {
        foreach (var m in messages) log.LogWarning("Push to {To}: {Title} — {Body}", m.To, m.Title, m.Body);
        return Task.CompletedTask;
    }
}

// Expo's push service delivers to both APNs and FCM from one call, using the token the app
// registers with expo-notifications. It accepts up to 100 messages per request.
public sealed class ExpoPushSender(HttpClient http, ILogger<ExpoPushSender> log) : IPushSender
{
    public const string Endpoint = "https://exp.host/--/api/v2/push/send";

    public async Task SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken ct = default)
    {
        foreach (var chunk in messages.Chunk(100))
        {
            var body = chunk.Select(m => new ExpoMessage(m.To, m.Title, m.Body)).ToArray();
            using var res = await http.PostAsJsonAsync(Endpoint, body, ct);
            if (!res.IsSuccessStatusCode)
                log.LogError("Expo push failed with {Status}: {Body}", (int)res.StatusCode, await res.Content.ReadAsStringAsync(ct));
        }
    }

    private sealed record ExpoMessage(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body)
    {
        [JsonPropertyName("sound")] public string Sound => "default";
    }
}
