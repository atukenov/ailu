using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ailu.Api.Data;
using Ailu.Api.Features.Auth;
using Ailu.Api.Features.Billing;
using Ailu.Api.Features.Goals;
using Ailu.Api.Features.Memories;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Ailu.Api.Tests;

public class BillingTests
{
    private static StorePurchase Apple(string id, string product = "kz.ailu.plus.monthly", int days = 30,
        SubscriptionStatus status = SubscriptionStatus.Active, string? account = null, DateTimeOffset? from = null) =>
        new(StoreProvider.Apple, id, product, status, (from ?? new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero)).AddDays(days),
            true, false, account);

    [Fact]
    public async Task One_partner_pays_and_both_get_the_plan()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var me = (await a.GetFromJsonAsync<UserDto>("/v1/me"))!;

        var before = (await b.GetFromJsonAsync<BillingDto>("/v1/billing"))!;
        Assert.Equal("free", before.Plan);
        Assert.True(before.TrialAvailable);
        Assert.Contains(before.Products, p => p is { Tier: "plus", PriceKzt: 1490, AppleProductId: "kz.ailu.plus.monthly" });

        // The app sends the latest transaction id; Apple answers with the original one.
        api.Apple.Purchases["txn-2"] = Apple("orig-1", account: me.Id.ToString());
        var res = await a.PostAsJsonAsync("/v1/billing/apple", new { transactionId = "txn-2" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var paid = (await res.Content.ReadFromJsonAsync<BillingDto>())!;
        Assert.Equal("plus", paid.Plan);
        Assert.True(paid.Subscription!.PaidByMe);

        var partner = (await b.GetFromJsonAsync<BillingDto>("/v1/billing"))!;
        Assert.Equal("plus", partner.Plan);
        Assert.False(partner.Subscription!.PaidByMe);
        Assert.Equal("plus", (await b.GetFromJsonAsync<StorageDto>("/v1/couple/storage"))!.Plan);
        Assert.Null((await b.GetFromJsonAsync<GoalList>("/v1/couple/goals"))!.Limit);

        // Restoring the same purchase again changes nothing and still works.
        api.Apple.Purchases["orig-1"] = api.Apple.Purchases["txn-2"];
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsJsonAsync("/v1/billing/apple", new { transactionId = "orig-1" })).StatusCode);
        using var scope = api.Services.CreateScope();
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<AiluDb>().Subscriptions.Count());
    }

    [Fact]
    public async Task A_purchase_stays_with_the_account_that_made_it()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var (c, _) = await api.PairAsync("+77010000003", "+77010000004");
        var aId = (await a.GetFromJsonAsync<UserDto>("/v1/me"))!.Id;

        // Bought from A's account: nobody else can claim it, not even on the same Apple ID.
        api.Apple.Purchases["t1"] = Apple("o1", account: aId.ToString());
        await AssertProblem(await c.PostAsJsonAsync("/v1/billing/apple", new { transactionId = "t1" }), HttpStatusCode.Conflict, "purchase_other_account");

        // Bought without an account id (an offer code redeemed in the App Store): the first claim wins.
        api.Apple.Purchases["t2"] = Apple("o2");
        Assert.Equal(HttpStatusCode.OK, (await b.PostAsJsonAsync("/v1/billing/apple", new { transactionId = "t2" })).StatusCode);
        await AssertProblem(await c.PostAsJsonAsync("/v1/billing/apple", new { transactionId = "t2" }), HttpStatusCode.Conflict, "purchase_other_account");
        Assert.Equal("free", (await c.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);

        await AssertProblem(await a.PostAsJsonAsync("/v1/billing/apple", new { transactionId = "nope" }), HttpStatusCode.NotFound, "purchase_not_found");
        api.Apple.Purchases["t3"] = Apple("o3", product: "com.other.app.pro");
        await AssertProblem(await a.PostAsJsonAsync("/v1/billing/apple", new { transactionId = "t3" }), HttpStatusCode.NotFound, "purchase_not_found");

        api.Apple.Down = true;
        await AssertProblem(await a.PostAsJsonAsync("/v1/billing/apple", new { transactionId = "t1" }), HttpStatusCode.ServiceUnavailable, "store_unavailable");
    }

    [Fact]
    public async Task Google_purchases_are_acknowledged_replaced_and_follow_notifications()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var end = api.Clock.GetUtcNow().AddDays(30);

        api.Google.Purchases["tok-1"] = new(StoreProvider.Google, "tok-1", "plus", SubscriptionStatus.Active, end, true, false,
            NeedsAcknowledge: true);
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsJsonAsync("/v1/billing/google", new { purchaseToken = "tok-1" })).StatusCode);
        Assert.Equal(["tok-1"], api.Google.Acknowledged);

        // Upgrading to Premium issues a new token that points at the old one.
        api.Google.Purchases["tok-2"] = new(StoreProvider.Google, "tok-2", "premium", SubscriptionStatus.Active, end, true, false,
            ReplacesStoreId: "tok-1");
        var upgraded = (await (await a.PostAsJsonAsync("/v1/billing/google", new { purchaseToken = "tok-2" })).Content.ReadFromJsonAsync<BillingDto>())!;
        Assert.Equal("premium", upgraded.Plan);
        Assert.Equal("premium", upgraded.Subscription!.Tier);

        // A refund, as Google's real-time notification announces it.
        api.Google.Purchases["tok-2"] = api.Google.Purchases["tok-2"] with { Status = SubscriptionStatus.Expired };
        var push = PubSub("tok-2");
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().PostAsJsonAsync("/v1/billing/webhooks/google?secret=wrong", push)).StatusCode);
        Assert.Equal("premium", (await b.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);
        Assert.Equal(HttpStatusCode.OK,
            (await api.CreateClient().PostAsJsonAsync($"/v1/billing/webhooks/google?secret={ApiFactory.GoogleWebhookSecret}", push)).StatusCode);
        var after = (await b.GetFromJsonAsync<BillingDto>("/v1/billing"))!;
        Assert.Equal("free", after.Plan); // tok-1 was replaced, so it doesn't count either
        Assert.Null(after.Subscription);
    }

    [Fact]
    public async Task Apple_notifications_only_make_the_server_ask_apple()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        api.Apple.Purchases["o1"] = Apple("o1");
        await a.PostAsJsonAsync("/v1/billing/apple", new { transactionId = "o1" });

        // A forged notification claiming a refund changes nothing while Apple still says active.
        var anon = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/v1/billing/webhooks/apple", new { signedPayload = AppleNotification("o1") })).StatusCode);
        Assert.Equal("plus", (await a.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);

        api.Apple.Purchases["o1"] = Apple("o1", status: SubscriptionStatus.Revoked);
        await anon.PostAsJsonAsync("/v1/billing/webhooks/apple", new { signedPayload = AppleNotification("o1") });
        Assert.Equal("free", (await a.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);

        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/v1/billing/webhooks/apple", new { signedPayload = "garbage" })).StatusCode);
    }

    [Fact]
    public async Task Expired_subscriptions_are_checked_once_more_then_end()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        api.Apple.Purchases["o1"] = Apple("o1", days: 30);
        await a.PostAsJsonAsync("/v1/billing/apple", new { transactionId = "o1" });

        // Renewed, but the notification got lost: the expiry job asks Apple and keeps the plan.
        api.Clock.Advance(TimeSpan.FromDays(31));
        api.Apple.Purchases["o1"] = Apple("o1", days: 61);
        Assert.Equal(1, await RunExpiry(api));
        a = await api.SignInAsync("+77010000001");
        Assert.Equal("plus", (await a.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);

        // Cancelled: at the end of the paid month the couple is back on Free, with nothing deleted.
        api.Clock.Advance(TimeSpan.FromDays(31));
        api.Apple.Purchases["o1"] = Apple("o1", days: 61, status: SubscriptionStatus.Expired);
        await RunExpiry(api);
        a = await api.SignInAsync("+77010000001");
        Assert.Equal("free", (await a.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);
        Assert.Equal(0, await RunExpiry(api));
    }

    [Fact]
    public async Task Trial_is_once_per_couple_even_after_pairing_again()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();

        var trial = (await (await b.PostAsync("/v1/billing/trial", null)).Content.ReadFromJsonAsync<BillingDto>())!;
        Assert.Equal("premium", trial.Plan);
        Assert.Equal("trial", trial.Subscription!.Provider);
        Assert.False(trial.TrialAvailable);
        await AssertProblem(await a.PostAsync("/v1/billing/trial", null), HttpStatusCode.Conflict, "trial_used");

        api.Clock.Advance(TimeSpan.FromDays(8));
        await RunExpiry(api);
        (a, b) = (await api.SignInAsync("+77010000001"), await api.SignInAsync("+77010000002"));
        Assert.Equal("free", (await a.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);

        // Splitting up and pairing again doesn't bring the trial back.
        await a.DeleteAsync("/v1/couple");
        (await a.PostAsJsonAsync("/v1/couple/invitations", new { phone = "+77010000002" })).EnsureSuccessStatusCode();
        (await b.PostAsync($"/v1/couple/invitations/{api.Sms.LastCode("+77010000002")}/accept", null)).EnsureSuccessStatusCode();
        Assert.False((await a.GetFromJsonAsync<BillingDto>("/v1/billing"))!.TrialAvailable);
    }

    [Fact]
    public async Task After_a_breakup_the_subscription_stays_with_the_payer()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        api.Apple.Purchases["o1"] = Apple("o1");
        await a.PostAsJsonAsync("/v1/billing/apple", new { transactionId = "o1" });

        await b.DeleteAsync("/v1/couple");
        var c = await api.SignInAsync("+77010000003", "Айгерим");
        var d = await api.SignInAsync("+77010000004", "Ерлан");
        (await a.PostAsJsonAsync("/v1/couple/invitations", new { phone = "+77010000003" })).EnsureSuccessStatusCode();
        (await c.PostAsync($"/v1/couple/invitations/{api.Sms.LastCode("+77010000003")}/accept", null)).EnsureSuccessStatusCode();
        (await b.PostAsJsonAsync("/v1/couple/invitations", new { phone = "+77010000004" })).EnsureSuccessStatusCode();
        (await d.PostAsync($"/v1/couple/invitations/{api.Sms.LastCode("+77010000004")}/accept", null)).EnsureSuccessStatusCode();

        Assert.Equal("plus", (await c.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);
        Assert.Equal("free", (await b.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);
    }

    [Fact]
    public void Apple_subscription_status_is_read_from_the_signed_transactions()
    {
        var expires = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var grace = expires.AddDays(16);
        var json = $$"""
            {"environment":"Sandbox","bundleId":"kz.ailu.app","data":[{"subscriptionGroupIdentifier":"1","lastTransactions":[
              {"originalTransactionId":"100","status":4,
               "signedTransactionInfo":"{{Signed(new { originalTransactionId = "100", transactionId = "105", productId = "kz.ailu.premium.monthly",
                   expiresDate = expires.ToUnixTimeMilliseconds(), appAccountToken = "7c9e6679-7425-40de-944b-e07fc1f90ae7" })}}",
               "signedRenewalInfo":"{{Signed(new { autoRenewStatus = 1, gracePeriodExpiresDate = grace.ToUnixTimeMilliseconds() })}}"}]}]}
            """;
        var p = AppleStoreClient.Parse(JsonDocument.Parse(json).RootElement, "kz.ailu.app")!;
        Assert.Equal(new StorePurchase(StoreProvider.Apple, "100", "kz.ailu.premium.monthly", SubscriptionStatus.Grace, grace, true, true,
            "7c9e6679-7425-40de-944b-e07fc1f90ae7"), p);
        Assert.Null(AppleStoreClient.Parse(JsonDocument.Parse(json).RootElement, "com.other.app"));
        Assert.Equal("100", AppleStoreClient.OriginalTransactionIdOf(AppleNotification("100")));
    }

    [Fact]
    public void Google_subscription_state_is_read_from_subscriptionsv2()
    {
        const string json = """
            {"subscriptionState":"SUBSCRIPTION_STATE_CANCELED","linkedPurchaseToken":"old","acknowledgementState":"ACKNOWLEDGEMENT_STATE_PENDING",
             "externalAccountIdentifiers":{"obfuscatedExternalAccountId":"u1"},"testPurchase":{},
             "lineItems":[{"productId":"plus","expiryTime":"2026-11-01T00:00:00.123Z","autoRenewingPlan":{"autoRenewEnabled":false}}]}
            """;
        var p = GoogleStoreClient.Parse(JsonDocument.Parse(json).RootElement, "tok")!;
        Assert.Equal(new StorePurchase(StoreProvider.Google, "tok", "plus", SubscriptionStatus.Active,
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, 123, TimeSpan.Zero), false, true, "u1", "old", true), p);
        Assert.Equal("tok-9", GoogleStoreClient.PurchaseTokenOf(JsonSerializer.SerializeToElement(PubSub("tok-9"))));
    }

    private static async Task<int> RunExpiry(ApiFactory api)
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BillingService>().RunExpiryAsync();
    }

    private static string Signed(object payload) =>
        $"{Base64UrlEncoder.Encode("""{"alg":"ES256"}""")}.{Base64UrlEncoder.Encode(JsonSerializer.Serialize(payload))}.sig";

    private static string AppleNotification(string originalTransactionId) => Signed(new
    {
        notificationType = "REFUND",
        data = new { signedTransactionInfo = Signed(new { originalTransactionId, productId = "kz.ailu.plus.monthly" }) }
    });

    private static object PubSub(string token) => new
    {
        message = new
        {
            data = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                packageName = "kz.ailu.app",
                subscriptionNotification = new { notificationType = 12, purchaseToken = token, subscriptionId = "plus" }
            }))),
            messageId = "1"
        },
        subscription = "projects/ailu/subscriptions/play"
    };

    private static async Task AssertProblem(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        Assert.Equal(status, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
    }
}
