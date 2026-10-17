using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Ailu.Api.Data;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ailu.Api.Infrastructure;

// Checks iOS purchases with the App Store Server API. The app sends a transaction id; the server asks
// Apple for the subscription's current state, so a made-up or replayed receipt gets nothing.
public sealed class AppleStoreClient(HttpClient http, AppleStoreOptions options, TimeProvider clock, ILogger<AppleStoreClient> log)
    : IStoreClient
{
    public const string Production = "https://api.storekit.itunes.apple.com";
    public const string Sandbox = "https://api.storekit-sandbox.itunes.apple.com";

    public StoreProvider Provider => StoreProvider.Apple;

    // Apple's advice: ask production first and fall back to sandbox, since TestFlight and App Review
    // purchases live in the sandbox even for the release build.
    public async Task<StorePurchase?> FetchAsync(string storeId, CancellationToken ct = default)
    {
        if (!options.Configured) throw new StoreUnavailableException("Apple purchases are not configured on this server.");
        return await FetchAsync(Production, storeId, ct) ?? await FetchAsync(Sandbox, storeId, ct);
    }

    // Apple subscriptions need no acknowledgement; finishing the transaction in the app is enough.
    public Task AcknowledgeAsync(StorePurchase purchase, CancellationToken ct = default) => Task.CompletedTask;

    private async Task<StorePurchase?> FetchAsync(string baseUrl, string transactionId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/inApps/v1/subscriptions/{Uri.EscapeDataString(transactionId)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct);
        }
        catch (HttpRequestException e)
        {
            throw new StoreUnavailableException("The App Store did not answer.", e);
        }
        using (res)
        {
            if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest) return null;
            if (!res.IsSuccessStatusCode)
            {
                log.LogError("App Store Server API returned {Status}: {Body}", (int)res.StatusCode, await res.Content.ReadAsStringAsync(ct));
                throw new StoreUnavailableException("The App Store did not answer.");
            }
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return Parse(doc.RootElement, options.BundleId);
        }
    }

    // The response of GET /inApps/v1/subscriptions/{transactionId}. The signed parts come to us straight from
    // Apple over TLS, so their payloads are read without checking the JWS chain again.
    public static StorePurchase? Parse(JsonElement root, string bundleId)
    {
        if (root.TryGetProperty("bundleId", out var bid) && bid.GetString() != bundleId) return null;
        var sandbox = root.TryGetProperty("environment", out var env) && env.GetString() == "Sandbox";

        StorePurchase? best = null;
        foreach (var group in root.GetProperty("data").EnumerateArray())
        foreach (var last in group.GetProperty("lastTransactions").EnumerateArray())
        {
            var txn = Jws.Payload(last.GetProperty("signedTransactionInfo").GetString()!);
            var renewal = last.TryGetProperty("signedRenewalInfo", out var r) ? Jws.Payload(r.GetString()!) : default;

            var periodEnd = Ms(txn, "expiresDate") ?? DateTimeOffset.MinValue;
            var status = last.GetProperty("status").GetInt32() switch
            {
                1 => SubscriptionStatus.Active,
                4 => SubscriptionStatus.Grace,
                5 => SubscriptionStatus.Revoked,
                _ => SubscriptionStatus.Expired // 2 expired, 3 billing retry without a grace period
            };
            if (txn.TryGetProperty("revocationDate", out _)) status = SubscriptionStatus.Revoked;
            if (status == SubscriptionStatus.Grace && renewal.ValueKind == JsonValueKind.Object
                && Ms(renewal, "gracePeriodExpiresDate") is { } graceEnd)
                periodEnd = graceEnd;

            var purchase = new StorePurchase(
                StoreProvider.Apple,
                txn.GetProperty("originalTransactionId").GetString()!,
                txn.GetProperty("productId").GetString()!,
                status,
                periodEnd,
                AutoRenew: renewal.ValueKind == JsonValueKind.Object && renewal.TryGetProperty("autoRenewStatus", out var ar) && ar.GetInt32() == 1,
                Sandbox: sandbox,
                AccountId: txn.TryGetProperty("appAccountToken", out var token) ? token.GetString() : null);

            // A family of products (Plus and Premium in one group) reports one entry per group; keep the
            // one that runs longest.
            if (best is null || purchase.PeriodEnd > best.PeriodEnd) best = purchase;
        }
        return best;
    }

    // Apple's App Store Server Notifications V2 body. Anyone can post to the webhook, so the payload is
    // used only to learn which subscription changed; its state is then fetched from Apple.
    public static string? OriginalTransactionIdOf(string signedPayload)
    {
        try
        {
            var payload = Jws.Payload(signedPayload);
            if (!payload.TryGetProperty("data", out var data) || !data.TryGetProperty("signedTransactionInfo", out var t)) return null;
            return Jws.Payload(t.GetString()!).GetProperty("originalTransactionId").GetString();
        }
        catch (Exception e) when (e is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Ms(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeMilliseconds(v.GetInt64()) : null;

    // A short-lived App Store Connect API token, signed with the account's ES256 key.
    private string Token()
    {
        var now = clock.GetUtcNow();
        using var ec = ECDsa.Create();
        ec.ImportFromPem(options.PrivateKey);
        var key = new ECDsaSecurityKey(ec) { KeyId = options.KeyId };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.IssuerId,
            Audience = "appstoreconnect-v1",
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(20).UtcDateTime,
            Claims = new Dictionary<string, object> { ["bid"] = options.BundleId },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256)
        });
    }
}

public static class Jws
{
    // The decoded payload of a compact JWS, without verifying it.
    public static JsonElement Payload(string jws)
    {
        var parts = jws.Split('.');
        if (parts.Length != 3) throw new FormatException("Not a compact JWS.");
        using var doc = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
        return doc.RootElement.Clone();
    }
}
