using System.Net;
using System.Net.Http.Json;
using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Features.Daily;
using Ailu.Api.Features.Goals;
using Ailu.Api.Features.Timeline;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ailu.Api.Tests;

public class TimelineTests
{
    private static async Task<TimelinePage> Timeline(HttpClient client, string? cursor = null) =>
        (await client.GetFromJsonAsync<TimelinePage>(
            "/v1/couple/timeline" + (cursor is null ? "" : $"?cursor={Uri.EscapeDataString(cursor)}")))!;

    private static async Task Memory(HttpClient client, string title, DateOnly? happenedOn = null) =>
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/v1/couple/memories", new { title, happenedOn })).StatusCode);

    private static async Task SetPlan(ApiFactory api, CouplePlan plan)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AiluDb>().Couples
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Plan, plan));
    }

    private static async Task Award(ApiFactory api, string sourceId, int amount)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
        var couple = await db.Couples.SingleAsync();
        await scope.ServiceProvider.GetRequiredService<NurLedgerService>()
            .AwardAsync(couple, NurSource.Achievement, sourceId, amount, CoupleCalendar.Today(couple, api.Clock));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_new_couple_starts_with_the_day_they_paired()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();

        var page = await Timeline(a);
        var start = Assert.Single(page.Items);
        Assert.Equal(TimelineKind.Start, start.Kind);
        Assert.Equal(new DateOnly(2026, 10, 1), start.Date);
        Assert.Equal(new TimelineSummary(1, 0, 0, 0), page.Summary);
        Assert.Equal(0, page.Hidden);
        Assert.Equal(TimelineEndpoints.FreeDays, page.FreeDays);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Firsts_memories_goals_and_phases_show_newest_first()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        api.Clock.Advance(TimeSpan.FromMinutes(5));

        var today = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        foreach (var (c, step) in new[] { (a, "accept"), (b, "accept"), (a, "complete"), (b, "complete") })
            (await c.PostAsync($"/v1/couple/tasks/{today.Task.Id}/{step}", null)).EnsureSuccessStatusCode();
        api.Clock.Advance(TimeSpan.FromMinutes(5));
        (await a.PostAsJsonAsync($"/v1/couple/questions/{today.Question.Id}/answer", new { text = "Море" })).EnsureSuccessStatusCode();
        (await b.PostAsJsonAsync($"/v1/couple/questions/{today.Question.Id}/answer", new { text = "Горы" })).EnsureSuccessStatusCode();

        api.Clock.Advance(TimeSpan.FromDays(1));
        a = await api.SignInAsync("+77010000001");
        b = await api.SignInAsync("+77010000002");
        await Memory(a, "Рим, первый вечер", new DateOnly(2026, 9, 20));
        var goal = (await (await a.PostAsJsonAsync("/v1/couple/goals",
            new { title = "Снять квартиру", category = "home", steps = new[] { "Переехать" } })).Content.ReadFromJsonAsync<GoalDto>())!;
        (await b.PostAsync($"/v1/couple/goals/{goal.Id}/join", null)).EnsureSuccessStatusCode();
        (await a.PostAsync($"/v1/couple/goals/{goal.Id}/steps/{goal.Steps[0].Id}/done", null)).EnsureSuccessStatusCode();
        (await b.PostAsync($"/v1/couple/goals/{goal.Id}/steps/{goal.Steps[0].Id}/done", null)).EnsureSuccessStatusCode();
        api.Clock.Advance(TimeSpan.FromMinutes(5));
        await Award(api, "big", 1_200); // crosses «Первые искры» at 500 and goes on to 1 380 in total

        var page = await Timeline(b);
        Assert.Equal(
            [TimelineKind.Phase, TimelineKind.GoalDone, TimelineKind.FirstAnswers, TimelineKind.FirstTask, TimelineKind.Start,
             TimelineKind.Memory],
            page.Items.Select(e => e.Kind));
        Assert.Equal(1, page.Items[0].Phase);
        Assert.Equal(1, page.Items[0].Cycle);
        Assert.Equal("Снять квартиру", page.Items[1].Title);
        Assert.Equal(goal.Id, page.Items[1].GoalId);
        var memory = page.Items[^1];
        Assert.Equal("Рим, первый вечер", memory.Title);
        Assert.Equal(new DateOnly(2026, 9, 20), memory.Date);
        Assert.Equal(0, memory.Photos);
        Assert.Null(memory.Thumb);
        Assert.Equal(new TimelineSummary(2, 1, 1, page.Summary.NurEarned), page.Summary);
        Assert.True(page.Summary.NurEarned >= 1_200 + Nur.TaskCompleted);
    }

    [Fact]
    public async Task One_award_that_crosses_two_phases_shows_both()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        await Award(api, "a", 400);
        await Award(api, "b", 1_200);

        var phases = (await Timeline(a)).Items.Where(e => e.Kind == TimelineKind.Phase).ToList();
        Assert.Equal([2, 1], phases.Select(e => e.Phase!.Value));
        Assert.Equal(2, phases.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public async Task Free_couples_see_their_last_90_days_and_the_start_paid_couples_see_everything()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        api.Clock.Advance(TimeSpan.FromDays(120));
        a = await api.SignInAsync("+77010000001");
        await Memory(a, "Давно", new DateOnly(2026, 10, 5));
        await Memory(a, "Недавно", new DateOnly(2027, 1, 20));

        var free = await Timeline(a);
        Assert.Equal([TimelineKind.Memory, TimelineKind.DaysTogether, TimelineKind.Start], free.Items.Select(e => e.Kind));
        Assert.Equal("Недавно", free.Items[0].Title);
        Assert.Equal(100, free.Items[1].Days);
        Assert.Equal(new DateOnly(2027, 1, 8), free.Items[1].Date);
        Assert.Equal(1, free.Hidden); // the October memory
        Assert.Equal(2, free.Summary.Memories);

        await SetPlan(api, CouplePlan.Plus);
        var plus = await Timeline(a);
        Assert.Null(plus.Hidden);
        Assert.Null(plus.FreeDays);
        Assert.Equal([TimelineKind.Memory, TimelineKind.DaysTogether, TimelineKind.Memory, TimelineKind.Start],
            plus.Items.Select(e => e.Kind));
        Assert.Equal("Давно", plus.Items[2].Title);
    }

    [Fact]
    public async Task Anniversaries_show_once_a_year()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Premium);
        api.Clock.Advance(TimeSpan.FromDays(2 * 365 + 10));
        a = await api.SignInAsync("+77010000001");

        var items = (await Timeline(a)).Items;
        Assert.Equal([2, 1], items.Where(e => e.Kind == TimelineKind.Anniversary).Select(e => e.Years!.Value));
        Assert.Equal([500, 100], items.Where(e => e.Kind == TimelineKind.DaysTogether).Select(e => e.Days!.Value));
        Assert.Equal(new DateOnly(2027, 10, 1), items.Single(e => e.Years == 1).Date);
    }

    [Fact]
    public async Task Pages_follow_on_without_gaps_or_repeats()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        for (var i = 0; i < 40; i++) await Memory(a, $"Вечер {i}", new DateOnly(2026, 9, 1 + i % 30));

        var first = await Timeline(a);
        Assert.Equal(30, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        var second = await Timeline(a, first.NextCursor);
        Assert.Null(second.NextCursor);

        var all = first.Items.Concat(second.Items).ToList();
        Assert.Equal(41, all.Count);
        Assert.Equal(41, all.Select(e => e.Id).Distinct().Count());
        Assert.Equal(all.OrderByDescending(e => e.Date).ThenByDescending(e => e.At).Select(e => e.Id), all.Select(e => e.Id));

        Assert.Equal(HttpStatusCode.BadRequest, (await a.GetAsync("/v1/couple/timeline?cursor=nope")).StatusCode);
    }

    [Fact]
    public async Task Needs_a_couple()
    {
        await using var api = await ApiFactory.StartAsync();
        var single = await api.SignInAsync("+77010000009", "Алмаз");
        Assert.Equal(HttpStatusCode.Conflict, (await single.GetAsync("/v1/couple/timeline")).StatusCode);
    }
}
