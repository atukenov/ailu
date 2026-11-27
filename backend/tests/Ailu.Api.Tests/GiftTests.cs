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
    public async Task The_team_makes_a_gift_code_and_it_gives_the_other_couple_the_plan()
    {
        await using var api = await ApiFactory.StartAsync();
        var (c, d) = await api.PairAsync("+77010000003", "+77010000004");

        // Gifts are sold person to person: only the team makes a gift code, after it has been paid.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().PostAsJsonAsync("/v1/admin/gift-codes",
            new { tier = "plus", months = 3 })).StatusCode);
        await AssertProblem(await Admin(api).PostAsJsonAsync("/v1/admin/gift-codes", new { tier = "gold", months = 3 }),
            HttpStatusCode.BadRequest, "invalid_tier");
        await AssertProblem(await Admin(api).PostAsJsonAsync("/v1/admin/gift-codes", new { tier = "plus", months = 0 }),
            HttpStatusCode.BadRequest, "invalid_months");
        // The app has no gift checkout any more.
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/v1/gifts")).StatusCode);

        var gift = await GiftCode(api, "plus", 3, "Айгерим, перевод 27.11");
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", gift);

        // Typed in lower case, without dashes, with O for zero: still the same gift.
        var typed = gift.Replace("-", " ").Replace('0', 'O').ToLowerInvariant();
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

        // The team sees it was entered, and nobody can enter it again.
        var listed = (await Admin(api).GetFromJsonAsync<List<GiftCodeDto>>("/v1/admin/gift-codes"))!.Single();
        Assert.True(listed.Redeemed);
        Assert.Equal("Айгерим, перевод 27.11", listed.Note);
        await AssertProblem(await d.PostAsJsonAsync("/v1/billing/redeem", new { code = gift }), HttpStatusCode.Conflict, "plan_code_used");
        var (e, _) = await api.PairAsync("+77010000005", "+77010000006");
        await AssertProblem(await e.PostAsJsonAsync("/v1/billing/redeem", new { code = gift }), HttpStatusCode.Conflict, "plan_code_used");
    }

    [Fact]
    public async Task A_gift_plan_ends_after_its_months_and_a_second_gift_follows_the_first()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var first = await GiftCode(api, "premium", 1);
        var second = await GiftCode(api, "premium", 1);
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
        await Redeem(a, await GiftCode(api, "plus", 1));
        Assert.False((await a.GetFromJsonAsync<BillingDto>("/v1/billing"))!.TrialAvailable);
        await AssertProblem(await a.PostAsync("/v1/billing/trial", null), HttpStatusCode.Conflict, "trial_used");
    }

    [Fact]
    public async Task Deleting_an_account_keeps_gift_records_and_unused_codes()
    {
        await using var api = await ApiFactory.StartAsync();
        var (c, _) = await api.PairAsync("+77010000003", "+77010000004");
        var unused = await GiftCode(api, "plus", 1);
        await Redeem(c, await GiftCode(api, "plus", 3));

        (await c.DeleteAsync("/v1/me")).EnsureSuccessStatusCode();

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
            Assert.Equal(2, await db.PlanCodes.CountAsync(p => p.Kind == PlanCodeKind.Gift));
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

    private static async Task<string> GiftCode(ApiFactory api, string tier, int months, string? note = null)
    {
        var res = await Admin(api).PostAsJsonAsync("/v1/admin/gift-codes", new { tier, months, note });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<GiftCodeDto>())!.Code;
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
