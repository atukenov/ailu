using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Features.Billing;
using Ailu.Api.Features.Gifts;
using Ailu.Api.Features.Memories;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ailu.Api.Tests;

public class GiftTests
{
    [Fact]
    public async Task A_gift_is_paid_then_its_code_gives_the_other_couple_the_plan()
    {
        await using var api = await ApiFactory.StartAsync();
        var (buyer, _) = await api.PairAsync();
        var (c, d) = await api.PairAsync("+77010000003", "+77010000004");

        var gifts = (await buyer.GetFromJsonAsync<GiftsDto>("/v1/gifts"))!;
        Assert.True(gifts.Available);
        Assert.Contains(gifts.Products, p => p is { Id: "plus_3m", Tier: "plus", Months: 3, PriceKzt: 4470 });
        Assert.Contains(gifts.Products, p => p is { Id: "premium_12m", Months: 12, PriceKzt: 23904 });

        var order = await Order(buyer, "plus_3m");
        Assert.Equal("pending", order.Status);
        Assert.Null(order.Code);
        Assert.StartsWith("https://pay.example/", order.PaymentUrl);

        // Paid on the provider's page; the buyer's screen asks again and gets the code.
        api.Checkout.PayAll();
        var paid = (await buyer.GetFromJsonAsync<GiftOrderDto>($"/v1/gifts/{order.Id}"))!;
        Assert.Equal("paid", paid.Status);
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", paid.Code);
        Assert.Null(paid.PaymentUrl);
        Assert.False(paid.Redeemed);

        // Typed in lower case, without dashes, with O for zero: still the same gift.
        var typed = paid.Code!.Replace("-", " ").Replace('0', 'O').ToLowerInvariant();
        var res = await c.PostAsJsonAsync("/v1/billing/redeem", new { code = typed });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var redeemed = (await res.Content.ReadFromJsonAsync<RedeemDto>())!;
        Assert.Equal("gift", redeemed.Kind);
        Assert.Equal("plus", redeemed.Tier);
        Assert.Equal(api.Clock.GetUtcNow().AddMonths(3), redeemed.PeriodEnd);

        // Both partners get it, like any subscription.
        var billing = (await d.GetFromJsonAsync<BillingDto>("/v1/billing"))!;
        Assert.Equal("plus", billing.Plan);
        Assert.Equal("gift", billing.Subscription!.Provider);
        Assert.False(billing.Subscription.AutoRenew);
        Assert.Equal(Plans.PlusPhotos, (await d.GetFromJsonAsync<StorageDto>("/v1/couple/storage"))!.PhotoLimit);

        // The buyer sees it was entered, and nobody can enter it again.
        Assert.True((await buyer.GetFromJsonAsync<GiftOrderDto>($"/v1/gifts/{order.Id}"))!.Redeemed);
        await AssertProblem(await d.PostAsJsonAsync("/v1/billing/redeem", new { code = paid.Code }), HttpStatusCode.Conflict, "plan_code_used");
        await AssertProblem(await buyer.PostAsJsonAsync("/v1/billing/redeem", new { code = paid.Code }), HttpStatusCode.Conflict, "plan_code_used");
        Assert.Equal("free", (await buyer.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);
    }

    [Fact]
    public async Task A_gift_plan_ends_after_its_months_and_a_second_gift_follows_the_first()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var first = await PaidCode(api, a, "premium_1m");
        var second = await PaidCode(api, a, "premium_1m");
        var start = api.Clock.GetUtcNow();

        await Redeem(a, first);
        var end = (await Redeem(a, second)).PeriodEnd;
        Assert.Equal(start.AddMonths(2), end);

        api.Clock.Advance(TimeSpan.FromDays(40));
        // The first month is over; the second keeps the plan. Codes are never asked about at a store.
        Assert.Equal(1, await RunExpiry(api));
        a = await api.SignInAsync("+77010000001");
        Assert.Equal("premium", (await a.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);

        api.Clock.Advance(TimeSpan.FromDays(25));
        await RunExpiry(api);
        a = await api.SignInAsync("+77010000001");
        Assert.Equal("free", (await a.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);
    }

    [Fact]
    public async Task A_refunded_gift_stops_working()
    {
        await using var api = await ApiFactory.StartAsync();
        var (buyer, _) = await api.PairAsync();
        var (c, _) = await api.PairAsync("+77010000003", "+77010000004");
        var order = await Order(buyer, "plus_1m");

        // The provider's notification is checked by its secret, then the payment is asked about.
        api.Checkout.PayAll();
        var anon = api.CreateClient();
        var hook = new { checkoutId = $"chk-{order.Id:N}" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/v1/gifts/webhooks/checkout?secret=nope", hook)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await anon.PostAsJsonAsync($"/v1/gifts/webhooks/checkout?secret={ApiFactory.GiftWebhookSecret}", hook)).StatusCode);
        var code = (await buyer.GetFromJsonAsync<GiftsDto>("/v1/gifts"))!.Orders.Single().Code!;
        await Redeem(c, code);
        Assert.Equal("plus", (await c.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);

        api.Checkout.Payments[hook.checkoutId] = GiftPaymentStatus.Refunded;
        await anon.PostAsJsonAsync($"/v1/gifts/webhooks/checkout?secret={ApiFactory.GiftWebhookSecret}", hook);
        Assert.Equal("free", (await c.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);
        var refunded = (await buyer.GetFromJsonAsync<GiftsDto>("/v1/gifts"))!.Orders.Single();
        Assert.Equal("refunded", refunded.Status);
        Assert.Null(refunded.Code);
        await AssertProblem(await c.PostAsJsonAsync("/v1/billing/redeem", new { code }), HttpStatusCode.NotFound, "plan_code_not_found");
    }

    [Fact]
    public async Task Without_a_payment_provider_gifts_are_not_for_sale()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        api.Checkout.Available = false;

        var gifts = (await a.GetFromJsonAsync<GiftsDto>("/v1/gifts"))!;
        Assert.False(gifts.Available);
        Assert.Empty(gifts.Products);
        await AssertProblem(await a.PostAsJsonAsync("/v1/gifts", new { productId = "plus_1m" }), HttpStatusCode.ServiceUnavailable, "gifts_unavailable");
        await AssertProblem(await a.PostAsJsonAsync("/v1/gifts", new { productId = "plus_7m" }), HttpStatusCode.BadRequest, "invalid_productId");

        // An order whose provider can't be reached stays open and is asked about next time.
        api.Checkout.Available = true;
        var order = await Order(a, "plus_1m");
        api.Checkout.Down = true;
        Assert.Equal("pending", (await a.GetFromJsonAsync<GiftsDto>("/v1/gifts"))!.Orders.Single(o => o.Id == order.Id).Status);
    }

    [Fact]
    public async Task A_promo_code_works_once_per_couple_until_it_runs_out()
    {
        await using var api = await ApiFactory.StartAsync();
        var admin = Admin(api);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().PostAsJsonAsync("/v1/admin/promo-codes",
            new { code = "LOVE2026", tier = "plus", days = 30 })).StatusCode);
        var created = await admin.PostAsJsonAsync("/v1/admin/promo-codes",
            new { code = "love-2026", tier = "plus", days = 30, maxRedemptions = 2, validUntil = api.Clock.GetUtcNow().AddDays(60), note = "Instagram" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("LOVE2026", (await created.Content.ReadFromJsonAsync<PromoDto>())!.Code);
        await AssertProblem(await admin.PostAsJsonAsync("/v1/admin/promo-codes", new { code = "LOVE2026", tier = "plus", days = 30 }),
            HttpStatusCode.Conflict, "plan_code_taken");

        var (a, b) = await api.PairAsync();
        var promo = await Redeem(a, "Love2026");
        Assert.Equal("promo", promo.Kind);
        Assert.Equal(api.Clock.GetUtcNow().AddDays(30), promo.PeriodEnd);
        Assert.Equal("plus", (await b.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);
        // The partner can't add the same 30 days again.
        await AssertProblem(await b.PostAsJsonAsync("/v1/billing/redeem", new { code = "LOVE2026" }), HttpStatusCode.Conflict, "plan_code_used");

        var (c, _) = await api.PairAsync("+77010000003", "+77010000004");
        await Redeem(c, "LOVE2026");
        var (e, _) = await api.PairAsync("+77010000005", "+77010000006");
        await AssertProblem(await e.PostAsJsonAsync("/v1/billing/redeem", new { code = "LOVE2026" }), HttpStatusCode.Conflict, "plan_code_ended");
        await AssertProblem(await e.PostAsJsonAsync("/v1/billing/redeem", new { code = "NOPE1234" }), HttpStatusCode.NotFound, "plan_code_not_found");
        await AssertProblem(await e.PostAsJsonAsync("/v1/billing/redeem", new { code = "ab" }), HttpStatusCode.BadRequest, "invalid_code");

        var listed = (await admin.GetFromJsonAsync<List<PromoDto>>("/v1/admin/promo-codes"))!;
        Assert.Equal(2, listed.Single().Redemptions);
    }

    [Fact]
    public async Task A_promo_code_past_its_date_is_refused()
    {
        await using var api = await ApiFactory.StartAsync();
        (await Admin(api).PostAsJsonAsync("/v1/admin/promo-codes",
            new { code = "SPRING", tier = "premium", months = 1, validUntil = api.Clock.GetUtcNow().AddDays(1) })).EnsureSuccessStatusCode();
        api.Clock.Advance(TimeSpan.FromDays(2));
        var (a, _) = await api.PairAsync();
        await AssertProblem(await a.PostAsJsonAsync("/v1/billing/redeem", new { code = "SPRING" }), HttpStatusCode.Conflict, "plan_code_ended");
    }

    [Fact]
    public async Task Codes_leave_the_trial_alone()
    {
        await using var api = await ApiFactory.StartAsync();
        (await Admin(api).PostAsJsonAsync("/v1/admin/promo-codes", new { code = "WELCOME", tier = "plus", days = 14 })).EnsureSuccessStatusCode();
        var (a, b) = await api.PairAsync();

        // A promo doesn't use up the trial...
        await Redeem(a, "WELCOME");
        Assert.True((await a.GetFromJsonAsync<BillingDto>("/v1/billing"))!.TrialAvailable);
        Assert.Equal(HttpStatusCode.OK, (await b.PostAsync("/v1/billing/trial", null)).StatusCode);
        var billing = (await a.GetFromJsonAsync<BillingDto>("/v1/billing"))!;
        Assert.Equal("premium", billing.Plan);
        Assert.False(billing.TrialAvailable);

        // ...and a couple that already had its trial can still enter a gift.
        var (buyer, _) = await api.PairAsync("+77010000003", "+77010000004");
        await Redeem(a, await PaidCode(api, buyer, "plus_1m"));
        Assert.False((await a.GetFromJsonAsync<BillingDto>("/v1/billing"))!.TrialAvailable);
        await AssertProblem(await a.PostAsync("/v1/billing/trial", null), HttpStatusCode.Conflict, "trial_used");
    }

    [Fact]
    public async Task Deleting_an_account_keeps_gift_records_and_unused_codes()
    {
        await using var api = await ApiFactory.StartAsync();
        var (buyer, _) = await api.PairAsync();
        var (c, _) = await api.PairAsync("+77010000003", "+77010000004");
        var unused = await PaidCode(api, buyer, "plus_1m");
        await Redeem(c, await PaidCode(api, buyer, "plus_3m"));

        (await buyer.DeleteAsync("/v1/me")).EnsureSuccessStatusCode();
        (await c.DeleteAsync("/v1/me")).EnsureSuccessStatusCode();

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
            Assert.Equal(2, await db.GiftOrders.CountAsync());
            Assert.Equal(1, await db.CodeRedemptions.CountAsync());
            Assert.Equal(1, await db.Subscriptions.CountAsync(s => s.Provider == StoreProvider.Gift));
        }

        // Whoever was given the other gift can still use it.
        var (e, _) = await api.PairAsync("+77010000005", "+77010000006");
        await Redeem(e, unused);
        Assert.Equal("plus", (await e.GetFromJsonAsync<BillingDto>("/v1/billing"))!.Plan);
    }

    [Fact]
    public async Task Entering_a_code_needs_a_couple()
    {
        await using var api = await ApiFactory.StartAsync();
        var single = await api.SignInAsync("+77010000009", "Дана");
        await AssertProblem(await single.PostAsJsonAsync("/v1/billing/redeem", new { code = "WELCOME" }), HttpStatusCode.Conflict, "no_couple");
    }

    private static HttpClient Admin(ApiFactory api)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ApiFactory.AdminKey);
        return client;
    }

    private static async Task<GiftOrderDto> Order(HttpClient client, string productId)
    {
        var res = await client.PostAsJsonAsync("/v1/gifts", new { productId });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<GiftOrderDto>())!;
    }

    private static async Task<string> PaidCode(ApiFactory api, HttpClient buyer, string productId)
    {
        var order = await Order(buyer, productId);
        api.Checkout.PayAll();
        return (await buyer.GetFromJsonAsync<GiftOrderDto>($"/v1/gifts/{order.Id}"))!.Code!;
    }

    private static async Task<RedeemDto> Redeem(HttpClient client, string code)
    {
        var res = await client.PostAsJsonAsync("/v1/billing/redeem", new { code });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<RedeemDto>())!;
    }

    private static async Task<int> RunExpiry(ApiFactory api)
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BillingService>().RunExpiryAsync();
    }

    private static async Task AssertProblem(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        Assert.Equal(status, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
    }
}
