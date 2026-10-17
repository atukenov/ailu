using System.Net;
using System.Net.Http.Json;
using Ailu.Api.Data;
using Ailu.Api.Features.Goals;
using Ailu.Api.Features.Pairing;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ailu.Api.Tests;

public class GoalTests
{
    private static async Task<GoalDto> Create(HttpClient client, string title = "Снять квартиру", params string[] steps)
    {
        var res = await client.PostAsJsonAsync("/v1/couple/goals",
            new { title, category = "home", targetMonth = "2026-12", steps });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<GoalDto>())!;
    }

    private static async Task<GoalDto> Shared(HttpClient a, HttpClient b, string title = "Снять квартиру", params string[] steps)
    {
        var goal = await Create(a, title, steps);
        return await Ok(await b.PostAsync($"/v1/couple/goals/{goal.Id}/join", null));
    }

    private static async Task<GoalDto> Ok(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<GoalDto>())!;
    }

    private static Task<HttpResponseMessage> Mark(HttpClient client, GoalDto goal, int step) =>
        client.PostAsync($"/v1/couple/goals/{goal.Id}/steps/{goal.Steps[step].Id}/done", null);

    private static async Task<int> Earned(HttpClient client) =>
        (await client.GetFromJsonAsync<PhaseDto>("/v1/couple/phase"))!.Earned;

    [Fact]
    public async Task Partner_sees_a_proposal_and_joins_it()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var goal = await Create(a, "Путешествие по Европе", "Загранпаспорта", "Билеты", "");

        Assert.Equal(GoalState.Proposed, goal.State);
        Assert.Equal("2026-12", goal.TargetMonth);
        Assert.Equal("home", goal.Category);
        Assert.True(goal.WeeklyReminder);
        Assert.Equal(["Загранпаспорта", "Билеты"], goal.Steps.Select(s => s.Title));

        var seen = (await b.GetFromJsonAsync<GoalList>("/v1/couple/goals"))!.Items.Single();
        Assert.Equal(GoalState.JoinNeeded, seen.State);
        Assert.False(seen.CreatedByMe);

        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsync($"/v1/couple/goals/{goal.Id}/join", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Mark(a, goal, 0)).StatusCode);

        var joined = await Ok(await b.PostAsync($"/v1/couple/goals/{goal.Id}/join", null));
        Assert.Equal(GoalState.Active, joined.State);
    }

    [Fact]
    public async Task A_step_closes_when_the_partner_confirms_and_earns_100_once()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var goal = await Shared(a, b, "Снять квартиру", "Накопить залог", "Посмотреть пять квартир", "Подписать договор", "Переехать");

        var marked = await Ok(await Mark(a, goal, 0));
        Assert.Equal(StepState.WaitingForPartner, marked.Steps[0].State);
        Assert.Equal(0, marked.Progress);
        await Ok(await Mark(a, goal, 0)); // marking twice confirms nothing
        Assert.Equal(0, await Earned(a));

        var partnerView = await b.GetFromJsonAsync<GoalDto>($"/v1/couple/goals/{goal.Id}");
        Assert.Equal(StepState.ConfirmNeeded, partnerView!.Steps[0].State);

        var closed = await Ok(await Mark(b, goal, 0));
        Assert.Equal(StepState.Done, closed.Steps[0].State);
        Assert.Equal(25, closed.Progress);
        Assert.Equal(100, await Earned(a));

        await Ok(await Mark(a, goal, 0));
        await Ok(await Mark(b, goal, 0));
        Assert.Equal(100, await Earned(a));
    }

    [Fact]
    public async Task Unconfirmed_mark_can_be_taken_back_only_by_whoever_made_it()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var goal = await Shared(a, b, "Поход в горы", "Купить палатку");
        await Ok(await Mark(a, goal, 0));

        var url = $"/v1/couple/goals/{goal.Id}/steps/{goal.Steps[0].Id}/done";
        Assert.Equal(StepState.ConfirmNeeded, (await Ok(await b.DeleteAsync(url))).Steps[0].State);
        Assert.Equal(StepState.Open, (await Ok(await a.DeleteAsync(url))).Steps[0].State);
    }

    [Fact]
    public async Task Goal_is_done_when_every_step_closes_and_frees_its_free_plan_slot()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var first = await Shared(a, b, "Купить машину", "Права");
        await Shared(a, b, "Поход в горы", "Палатка");

        var list = (await a.GetFromJsonAsync<GoalList>("/v1/couple/goals"))!;
        Assert.Equal((2, 2, false), (list.Limit!.Value, list.Unfinished, list.CanCreate));
        var refused = await a.PostAsJsonAsync("/v1/couple/goals", new { title = "Третья" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("goal_limit", await refused.Content.ReadAsStringAsync());

        await Ok(await Mark(a, first, 0));
        var done = await Ok(await Mark(b, first, 0));
        Assert.Equal((GoalState.Done, 100), (done.State, done.Progress));
        Assert.NotNull(done.CompletedAt);

        list = (await a.GetFromJsonAsync<GoalList>("/v1/couple/goals"))!;
        Assert.True(list.CanCreate);
        Assert.Equal("Купить машину", list.Items[^1].Title); // finished goals go last
        await Create(a, "Третья");

        // A new step reopens a finished goal.
        var reopened = await Ok(await a.PostAsJsonAsync($"/v1/couple/goals/{first.Id}/steps", new { title = "Страховка" }));
        Assert.Equal((GoalState.Active, 50), (reopened.State, reopened.Progress));
    }

    [Fact]
    public async Task Plus_couples_have_no_goal_limit()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AiluDb>().Couples
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Plan, CouplePlan.Plus));

        for (var i = 0; i < 4; i++) await Create(a, $"Цель {i}");
        var list = (await a.GetFromJsonAsync<GoalList>("/v1/couple/goals"))!;
        Assert.Null(list.Limit);
        Assert.True(list.CanCreate);
    }

    [Fact]
    public async Task Two_goals_created_at_once_cannot_both_take_the_last_free_slot()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await Create(a, "Первая");

        var results = await Task.WhenAll(
            a.PostAsJsonAsync("/v1/couple/goals", new { title = "Вторая" }),
            b.PostAsJsonAsync("/v1/couple/goals", new { title = "Тоже вторая" }));
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], results.Select(r => r.StatusCode).Order());
    }

    [Fact]
    public async Task Removing_a_shared_goal_takes_both_partners()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var goal = await Shared(a, b, "Снять квартиру", "Залог");
        var url = $"/v1/couple/goals/{goal.Id}";

        var asked = await Ok(await a.DeleteAsync(url));
        Assert.True(asked.RemovalRequestedByMe);
        Assert.True((await b.GetFromJsonAsync<GoalDto>(url))!.RemovalRequestedByPartner);

        // The partner keeps it; asking again starts over.
        Assert.False((await Ok(await b.PostAsync($"{url}/keep", null))).RemovalRequestedByPartner);
        await Ok(await a.DeleteAsync(url));
        await Ok(await a.DeleteAsync(url)); // the same partner asking twice is still one vote

        Assert.Equal(HttpStatusCode.NoContent, (await b.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync(url)).StatusCode);
        Assert.Empty((await a.GetFromJsonAsync<GoalList>("/v1/couple/goals"))!.Items);
    }

    [Fact]
    public async Task A_proposal_nobody_joined_can_be_withdrawn_alone()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var goal = await Create(a);
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/v1/couple/goals/{goal.Id}")).StatusCode);
    }

    [Fact]
    public async Task Closed_steps_stay_and_open_ones_can_change()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var goal = await Shared(a, b, "Здоровье", "Анализы", "Абонемент");
        await Ok(await Mark(a, goal, 0));
        await Ok(await Mark(b, goal, 0));

        var closedUrl = $"/v1/couple/goals/{goal.Id}/steps/{goal.Steps[0].Id}";
        Assert.Equal(HttpStatusCode.Conflict, (await a.DeleteAsync(closedUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PatchAsJsonAsync(closedUrl, new { title = "x" })).StatusCode);

        var openUrl = $"/v1/couple/goals/{goal.Id}/steps/{goal.Steps[1].Id}";
        Assert.Equal("Бассейн", (await Ok(await b.PatchAsJsonAsync(openUrl, new { title = "Бассейн" }))).Steps[1].Title);

        // Removing the last open step leaves only closed ones: the goal is done.
        var done = await Ok(await a.DeleteAsync(openUrl));
        Assert.Equal((GoalState.Done, 100, 1), (done.State, done.Progress, done.StepsTotal));
    }

    [Fact]
    public async Task Either_partner_edits_and_bad_fields_are_refused()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var goal = await Shared(a, b);
        var url = $"/v1/couple/goals/{goal.Id}";

        var edited = await Ok(await b.PatchAsJsonAsync(url, new { title = "Своя квартира", category = "money", targetMonth = "", weeklyReminder = false }));
        Assert.Equal(("Своя квартира", "money", (string?)null, false), (edited.Title, edited.Category, edited.TargetMonth, edited.WeeklyReminder));

        Assert.Equal(HttpStatusCode.BadRequest, (await a.PatchAsJsonAsync(url, new { category = "cars" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PatchAsJsonAsync(url, new { category = "3" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PatchAsJsonAsync(url, new { targetMonth = "2026-09" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PatchAsJsonAsync(url, new { title = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/v1/couple/goals", new { title = "" })).StatusCode);
    }

    [Fact]
    public async Task Another_couple_cannot_see_or_touch_the_goal()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var goal = await Shared(a, b, "Наша", "Шаг");
        var (c, _) = await api.PairAsync("+77010000003", "+77010000004");

        Assert.Empty((await c.GetFromJsonAsync<GoalList>("/v1/couple/goals"))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/v1/couple/goals/{goal.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Mark(c, goal, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/v1/couple/goals/{goal.Id}")).StatusCode);
    }

    [Fact]
    public async Task Sunday_evening_reminds_both_of_a_goal_once()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        (await a.PutAsJsonAsync("/v1/me/devices", new { pushToken = "ExponentPushToken[a]", platform = "ios" })).EnsureSuccessStatusCode();
        (await b.PutAsJsonAsync("/v1/me/devices", new { pushToken = "ExponentPushToken[b]", platform = "android" })).EnsureSuccessStatusCode();
        var goal = await Shared(a, b, "Поход в горы", "Палатка", "Маршрут");
        await Create(a, "Без партнёра"); // not joined: no reminder

        // 2026-10-04 is a Sunday; 20:00 in Almaty is 15:00 UTC.
        api.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 3, 15, 0, 0, TimeSpan.Zero));
        await RunJob(api);
        Assert.Empty(api.Push.Sent);

        api.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 4, 15, 0, 0, TimeSpan.Zero));
        await RunJob(api);
        await RunJob(api);
        Assert.Equal(2, api.Push.Sent.Count);
        Assert.All(api.Push.Sent, p => Assert.Equal("«Поход в горы» — 0%. Какой шаг сделаете на этой неделе?", p.Body));

        // Turned off, it stays quiet the next Sunday.
        a = await api.SignInAsync("+77010000001"); // the first access token has expired by now
        (await a.PatchAsJsonAsync($"/v1/couple/goals/{goal.Id}", new { weeklyReminder = false })).EnsureSuccessStatusCode();
        api.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 11, 15, 0, 0, TimeSpan.Zero));
        await RunJob(api);
        Assert.Equal(2, api.Push.Sent.Count(p => p.Title == "Ваша цель"));
    }

    private static async Task RunJob(ApiFactory api)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DailyPushJob>().RunOnceAsync();
    }
}
