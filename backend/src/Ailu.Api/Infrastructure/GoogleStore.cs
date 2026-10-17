using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ailu.Api.Data;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ailu.Api.Infrastructure;

// Checks Android purchases with the Google Play Developer API (purchases.subscriptionsv2). The app sends
// the purchase token; the server asks Google what it is worth and acknowledges it, or Google refunds it
// after three days.
public sealed class GoogleStoreClient(HttpClient http, GoogleStoreOptions options, TimeProvider clock, ILogger<GoogleStoreClient> log)
    : IStoreClient
{
    private const string Api = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications";
    private const string Scope = "https://www.googleapis.com/auth/androidpublisher";

    public StoreProvider Provider => StoreProvider.Google;

    public async Task<StorePurchase?> FetchAsync(string storeId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"{Api}/{options.PackageName}/purchases/subscriptionsv2/tokens/{Uri.EscapeDataString(storeId)}");
        using var res = await SendAsync(req, ct);
        if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.BadRequest) return null;
        await EnsureOk(res, ct);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return Parse(doc.RootElement, storeId);
    }

    public async Task AcknowledgeAsync(StorePurchase purchase, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{Api}/{options.PackageName}/purchases/subscriptions/{Uri.EscapeDataString(purchase.ProductId)}/tokens/{Uri.EscapeDataString(purchase.StoreId)}:acknowledge")
        {
            Content = JsonContent.Create(new { })
        };
        using var res = await SendAsync(req, ct);
        await EnsureOk(res, ct);
    }

    // A SubscriptionPurchaseV2 resource.
    public static StorePurchase? Parse(JsonElement root, string token)
    {
        if (!root.TryGetProperty("lineItems", out var items) || items.GetArrayLength() == 0) return null;

        // One line item per product in the purchase; Ailu sells one at a time, so take the latest-ending.
        var item = items.EnumerateArray().MaxBy(i => Time(i, "expiryTime") ?? DateTimeOffset.MinValue);
        var periodEnd = Time(item, "expiryTime") ?? DateTimeOffset.MinValue;
        var status = root.GetProperty("subscriptionState").GetString() switch
        {
            // A cancelled subscription still runs to the end of the period it paid for.
            "SUBSCRIPTION_STATE_ACTIVE" or "SUBSCRIPTION_STATE_CANCELED" => SubscriptionStatus.Active,
            "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" => SubscriptionStatus.Grace,
            "SUBSCRIPTION_STATE_PENDING" => SubscriptionStatus.Pending,
            _ => SubscriptionStatus.Expired // on hold, paused, expired, pending purchase cancelled
        };
        var autoRenew = item.TryGetProperty("autoRenewingPlan", out var plan)
            && plan.TryGetProperty("autoRenewEnabled", out var ar) && ar.GetBoolean();
        string? account = null;
        if (root.TryGetProperty("externalAccountIdentifiers", out var ids) && ids.TryGetProperty("obfuscatedExternalAccountId", out var a))
            account = a.GetString();

        return new StorePurchase(
            StoreProvider.Google,
            token,
            item.GetProperty("productId").GetString()!,
            status,
            periodEnd,
            autoRenew,
            Sandbox: root.TryGetProperty("testPurchase", out _),
            AccountId: account,
            ReplacesStoreId: root.TryGetProperty("linkedPurchaseToken", out var linked) ? linked.GetString() : null,
            NeedsAcknowledge: root.TryGetProperty("acknowledgementState", out var ack)
                && ack.GetString() == "ACKNOWLEDGEMENT_STATE_PENDING" && status is SubscriptionStatus.Active or SubscriptionStatus.Grace);
    }

    // The Pub/Sub push body of a real-time developer notification. Like Apple's, it only says which
    // purchase token changed; its state is then fetched from Google.
    public static string? PurchaseTokenOf(JsonElement pushBody)
    {
        try
        {
            var data = pushBody.GetProperty("message").GetProperty("data").GetString()!;
            using var doc = JsonDocument.Parse(Convert.FromBase64String(data));
            return doc.RootElement.TryGetProperty("subscriptionNotification", out var n) ? n.GetProperty("purchaseToken").GetString() : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Time(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && DateTimeOffset.TryParse(v.GetString(), out var t) ? t : null;

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (!options.Configured) throw new StoreUnavailableException("Google Play purchases are not configured on this server.");
        try
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
            return await http.SendAsync(req, ct);
        }
        catch (HttpRequestException e)
        {
            throw new StoreUnavailableException("Google Play did not answer.", e);
        }
    }

    private async Task EnsureOk(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        log.LogError("Google Play Developer API returned {Status}: {Body}", (int)res.StatusCode, await res.Content.ReadAsStringAsync(ct));
        throw new StoreUnavailableException("Google Play did not answer.");
    }

    // OAuth for a service account: a signed JWT exchanged for an access token.
    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        using var account = JsonDocument.Parse(options.ServiceAccountJson);
        var email = account.RootElement.GetProperty("client_email").GetString()!;
        var tokenUri = account.RootElement.TryGetProperty("token_uri", out var t) ? t.GetString()! : "https://oauth2.googleapis.com/token";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(account.RootElement.GetProperty("private_key").GetString());

        var now = clock.GetUtcNow();
        var assertion = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = email,
            Audience = tokenUri,
            IssuedAt = now.UtcDateTime,
            Expires = now.AddMinutes(30).UtcDateTime,
            Claims = new Dictionary<string, object> { ["scope"] = Scope },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
        });
        using var res = await http.PostAsync(tokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = assertion
        }), ct);
        await EnsureOk(res, ct);
        using var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return body.RootElement.GetProperty("access_token").GetString()!;
    }
}
