using System.Net;
using System.Net.Http.Json;
using Ailu.Api.Data;
using Ailu.Api.Features.Achievements;
using Ailu.Api.Features.Memories;
using Ailu.Api.Features.Pairing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ailu.Api.Tests;

public class AchievementTests
{
    private const string Url = "/v1/couple/achievements";

    private static async Task<AchievementDto> Ok(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<AchievementDto>())!;
    }

    private static Task<HttpResponseMessage> Mark(HttpClient client, string slug, object? body = null) =>
        client.PostAsJsonAsync($"{Url}/{slug}/done", body ?? new { });

    private static async Task<int> Earned(HttpClient client) =>
        (await client.GetFromJsonAsync<PhaseDto>("/v1/couple/phase"))!.Earned;

    private static async Task SetPlan(ApiFactory api, CouplePlan plan)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AiluDb>().Couples.ExecuteUpdateAsync(s => s.SetProperty(c => c.Plan, plan));
    }

    [Fact]
    public async Task Free_couples_see_the_whole_library_with_only_the_firsts_open()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();

        var list = (await a.GetFromJsonAsync<AchievementList>(Url))!;
        Assert.Equal((0, Seed.Achievements.Length, "free"), (list.Earned, list.Total, list.Plan));
        Assert.Equal(Seed.Achievements.Length, list.Items.Count);
        Assert.All(list.Items.Where(x => x.Category == "firsts"), x => Assert.Equal(AchievementState.Open, x.State));
        Assert.All(list.Items.Where(x => x.Category != "firsts"), x => Assert.Equal(AchievementState.Locked, x.State));
        Assert.Equal("Первое кофе", list.Items[0].Title);

        var travel = (await a.GetFromJsonAsync<AchievementList>($"{Url}?category=travel"))!;
        Assert.NotEmpty(travel.Items);
        Assert.All(travel.Items, x => Assert.Equal("travel", x.Category));
        Assert.Equal(HttpStatusCode.BadRequest, (await a.GetAsync($"{Url}?category=1")).StatusCode);

        var locked = await Mark(a, "first-trip");
        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
        Assert.Contains("achievement_locked", await locked.Content.ReadAsStringAsync());

        await SetPlan(api, CouplePlan.Plus);
        Assert.Equal(AchievementState.WaitingForPartner, (await Ok(await Mark(a, "first-trip"))).State);
    }

    [Fact]
    public async Task Earned_when_the_partner_confirms_with_80_nur_once()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();

        var marked = await Ok(await Mark(a, "first-coffee", new { happenedOn = "2026-09-20" }));
        Assert.Equal((AchievementState.WaitingForPartner, "2026-09-20"), (marked.State, marked.HappenedOn));
        await Ok(await Mark(a, "first-coffee")); // marking again confirms nothing
        Assert.Equal(0, await Earned(a));

        var seen = (await b.GetFromJsonAsync<AchievementDto>($"{Url}/first-coffee"))!;
        Assert.Equal(AchievementState.ConfirmNeeded, seen.State);

        var earned = await Ok(await Mark(b, "first-coffee"));
        Assert.Equal((AchievementState.Earned, "2026-09-20"), (earned.State, earned.HappenedOn));
        Assert.NotNull(earned.EarnedAt);
        Assert.Equal(80, await Earned(a));

        await Ok(await Mark(a, "first-coffee"));
        await Ok(await Mark(b, "first-coffee"));
        Assert.Equal(80, await Earned(a));
        Assert.Equal(1, (await a.GetFromJsonAsync<AchievementList>(Url))!.Earned);
    }

    [Fact]
    public async Task Earned_achievement_is_never_undone_even_when_the_plan_lapses()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Plus);
        await Ok(await Mark(a, "mountains"));
        await Ok(await Mark(a, "abroad"));
        await Ok(await Mark(b, "mountains"));

        var refused = await a.DeleteAsync($"{Url}/mountains/done");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // Back on Free: the earned one stays, and the one marked under Plus can still be confirmed.
        await SetPlan(api, CouplePlan.Free);
        var list = (await a.GetFromJsonAsync<AchievementList>(Url))!;
        Assert.Equal(AchievementState.Earned, list.Items.Single(x => x.Slug == "mountains").State);
        Assert.Equal(AchievementState.Earned, (await Ok(await Mark(b, "abroad"))).State);
        Assert.Equal(160, await Earned(a));
    }

    [Fact]
    public async Task Either_partner_can_clear_an_unconfirmed_mark()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();

        await Ok(await Mark(a, "sunrise"));
        Assert.Equal(AchievementState.Open, (await Ok(await b.DeleteAsync($"{Url}/sunrise/done"))).State);
        await Ok(await Mark(b, "sunrise"));
        Assert.Equal(AchievementState.Open, (await Ok(await b.DeleteAsync($"{Url}/sunrise/done"))).State);
        Assert.Equal(AchievementState.Open, (await a.GetFromJsonAsync<AchievementDto>($"{Url}/sunrise"))!.State);
    }

    [Fact]
    public async Task Date_must_not_be_in_the_future_and_a_memory_must_be_the_couples()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var (c, _) = await api.PairAsync("+77010000003", "+77010000004");

        Assert.Equal(HttpStatusCode.BadRequest, (await Mark(a, "first-movie", new { happenedOn = "2026-10-02" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Mark(a, "first-movie", new { happenedOn = "вчера" })).StatusCode);

        var created = await c.PostAsJsonAsync("/v1/couple/memories", new { title = "Чужое", happenedOn = "2026-09-30" });
        var theirs = (await created.Content.ReadFromJsonAsync<MemoryDto>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await Mark(a, "first-movie", new { memoryId = theirs.Id })).StatusCode);

        var mine = (await (await a.PostAsJsonAsync("/v1/couple/memories", new { title = "Кино", happenedOn = "2026-09-30" }))
            .Content.ReadFromJsonAsync<MemoryDto>())!;
        await Ok(await Mark(a, "first-movie", new { happenedOn = "2026-09-30" }));
        var earned = await Ok(await Mark(b, "first-movie"));
        Assert.Null(earned.MemoryId);

        // A memory can be added to an earned achievement; its date stays.
        var linked = await Ok(await Mark(a, "first-movie", new { memoryId = mine.Id, happenedOn = "2026-09-01" }));
        Assert.Equal((mine.Id, "2026-09-30"), (linked.MemoryId!.Value, linked.HappenedOn));
        Assert.Equal(HttpStatusCode.NotFound, (await Mark(a, "no-such-thing")).StatusCode);
    }

    [Fact]
    public async Task Library_texts_follow_the_callers_language()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        (await b.PatchAsJsonAsync("/v1/me", new { locale = "kk" })).EnsureSuccessStatusCode();

        Assert.Equal("Первое кофе", (await a.GetFromJsonAsync<AchievementDto>($"{Url}/first-coffee"))!.Title);
        Assert.Equal("Алғашқы кофе", (await b.GetFromJsonAsync<AchievementDto>($"{Url}/first-coffee"))!.Title);
    }

    [Fact]
    public void Every_achievement_has_both_languages_and_a_unique_slug()
    {
        Assert.Equal(Seed.Achievements.Length, Seed.Achievements.Select(x => x.Slug).Distinct().Count());
        Assert.All(Seed.Achievements, x =>
        {
            Assert.All(new[] { x.Title, x.Body, x.TitleKk, x.BodyKk, x.Icon }, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        });
        // Free couples always have something to collect.
        Assert.Contains(Seed.Achievements, x => x.Tier == CouplePlan.Free);
    }
}
