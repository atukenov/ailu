using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ailu.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ailu.Api.Infrastructure;

// To is a Firebase Cloud Messaging registration token (the app gets one on iOS and Android alike).
public sealed record PushMessage(string To, string Title, string Body);

public interface IPushSender
{
    Task SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken ct = default);
}

// Until Firebase is configured, pushes go to the log. Never run production like this.
public sealed class LoggingPushSender(ILogger<LoggingPushSender> log) : IPushSender
{
    public Task SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken ct = default)
    {
        foreach (var m in messages) log.LogWarning("Push to {To}: {Title} — {Body}", m.To, m.Title, m.Body);
        return Task.CompletedTask;
    }
}

public sealed class FirebaseOptions
{
    // The Firebase service account key (Project settings → Service accounts → Generate new private key),
    // the whole JSON file as one value. Push:Firebase:ServiceAccountJson, or Push__Firebase__ServiceAccountJson.
    public string ServiceAccountJson { get; set; } = "";

    // Optional: taken from project_id in the key when empty.
    public string ProjectId { get; set; } = "";

    // At most this many FCM requests in flight at once (FCM v1 sends one message per request).
    public int Parallelism { get; set; } = 8;

    public bool Configured => ServiceAccountJson.Trim() != "";
}

// Removes tokens FCM says are gone (app uninstalled, token rotated), so we stop sending to them.
public interface IPushTokenCleanup
{
    Task RemoveAsync(IReadOnlyCollection<string> tokens, CancellationToken ct = default);
}

public sealed class DbPushTokenCleanup(IServiceScopeFactory scopes) : IPushTokenCleanup
{
    public async Task RemoveAsync(IReadOnlyCollection<string> tokens, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
        await db.Devices.Where(d => tokens.Contains(d.PushToken)).ExecuteDeleteAsync(ct);
    }
}

// Firebase Cloud Messaging, HTTP v1 API. Android goes through FCM directly; iOS goes through FCM to
// APNs, using the APNs auth key uploaded in the Firebase console. One request per message.
public sealed class FcmPushSender(HttpClient http, FirebaseOptions options, IPushTokenCleanup cleanup, TimeProvider clock,
    ILogger<FcmPushSender> log) : IPushSender
{
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? accessToken;
    private DateTimeOffset accessTokenExpires;
    private ServiceAccount? account;

    public static string Endpoint(string projectId) => $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send";

    public async Task SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return;
        var key = Account();
        var bearer = await AccessTokenAsync(ct);
        var endpoint = Endpoint(options.ProjectId != "" ? options.ProjectId : key.ProjectId);

        var dead = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(messages,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Parallelism), CancellationToken = ct },
            async (m, token) =>
            {
                try
                {
                    if (await SendOneAsync(endpoint, bearer, m, token) == Outcome.Unregistered) dead.Add(m.To);
                }
                catch (HttpRequestException e)
                {
                    log.LogError(e, "FCM did not answer");
                }
            });

        if (!dead.IsEmpty)
        {
            log.LogInformation("Removing {Count} push tokens FCM no longer knows", dead.Count);
            await cleanup.RemoveAsync(dead.Distinct().ToList(), ct);
        }
    }

    private enum Outcome { Sent, Unregistered, Failed }

    private async Task<Outcome> SendOneAsync(string endpoint, string bearer, PushMessage m, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(Payload(m)) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var res = await http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return Outcome.Sent;

        var body = await res.Content.ReadAsStringAsync(ct);
        // 404 UNREGISTERED: the app was removed or the token rotated. 400 INVALID_ARGUMENT naming the
        // token: it was never a valid FCM token (for example an old Expo token from before Firebase).
        if (res.StatusCode == HttpStatusCode.NotFound || (res.StatusCode == HttpStatusCode.BadRequest && IsBadToken(body)))
            return Outcome.Unregistered;

        log.LogError("FCM returned {Status}: {Body}", (int)res.StatusCode, body);
        return Outcome.Failed;
    }

    private static bool IsBadToken(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var error = doc.RootElement.GetProperty("error");
            if (error.TryGetProperty("details", out var details))
                foreach (var d in details.EnumerateArray())
                    if (d.TryGetProperty("errorCode", out var code) && code.GetString() == "UNREGISTERED") return true;
            return error.TryGetProperty("message", out var msg)
                   && msg.GetString()?.Contains("registration token", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    // Shown as a banner with sound on both platforms. Android uses the "default" channel the app creates.
    public static object Payload(PushMessage m) => new
    {
        message = new
        {
            token = m.To,
            notification = new { title = m.Title, body = m.Body },
            android = new { priority = "high", notification = new { channel_id = "default", sound = "default" } },
            apns = new { payload = new { aps = new { sound = "default" } } }
        }
    };

    private sealed record ServiceAccount(string Email, string PrivateKey, string TokenUri, string ProjectId);

    private ServiceAccount Account()
    {
        if (account is not null) return account;
        using var doc = JsonDocument.Parse(options.ServiceAccountJson);
        var r = doc.RootElement;
        return account = new ServiceAccount(
            r.GetProperty("client_email").GetString()!,
            r.GetProperty("private_key").GetString()!,
            r.TryGetProperty("token_uri", out var t) ? t.GetString()! : "https://oauth2.googleapis.com/token",
            r.TryGetProperty("project_id", out var p) ? p.GetString()! : "");
    }

    // OAuth for a service account: a signed JWT exchanged for an access token, kept until near expiry.
    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        await tokenLock.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (accessToken is not null && now < accessTokenExpires) return accessToken;

            var key = Account();
            using var rsa = RSA.Create();
            rsa.ImportFromPem(key.PrivateKey);
            var assertion = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = key.Email,
                Audience = key.TokenUri,
                IssuedAt = now.UtcDateTime,
                Expires = now.AddMinutes(60).UtcDateTime,
                Claims = new Dictionary<string, object> { ["scope"] = Scope },
                // No provider cache: it would keep this RSA object after it is disposed, and the next sign fails.
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            }
            });
            using var res = await http.PostAsync(key.TokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion
            }), ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Google OAuth returned {(int)res.StatusCode}: {text}");

            using var body = JsonDocument.Parse(text);
            var seconds = body.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
            accessToken = body.RootElement.GetProperty("access_token").GetString()!;
            accessTokenExpires = now.AddSeconds(seconds - 300);
            return accessToken;
        }
        finally
        {
            tokenLock.Release();
        }
    }
}
