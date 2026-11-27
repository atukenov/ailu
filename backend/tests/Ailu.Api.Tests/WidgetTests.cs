using System.Net;
using System.Net.Http.Json;
using Ailu.Api.Domain;
using Ailu.Api.Features.Daily;
using Ailu.Api.Features.Widget;

namespace Ailu.Api.Tests;

public class WidgetTests
{
    [Fact]
    public async Task Widget_follows_the_day_from_offered_to_done()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();

        var w = (await a.GetFromJsonAsync<WidgetDto>("/v1/couple/widget"))!;
        Assert.Equal(new DateOnly(2026, 10, 1), w.Date);
        // Midnight in Almaty (UTC+5) is 19:00 UTC the day before.
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 19, 0, 0, TimeSpan.Zero), w.NextDayAt.ToUniversalTime());
        Assert.Equal(1, w.DaysTogether);
        Assert.Equal("Нурай", w.PartnerName);
        Assert.Equal("free", w.Plan);
        Assert.Equal(0, w.Streak);
        Assert.Equal(TaskState.Offered, w.Task.State);
        Assert.Equal(0, w.Task.Done);
        Assert.Equal(1, w.Task.PerDay);
        Assert.False(w.Question.Answered);

        // The widget and the Today screen show the same task.
        var today = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        Assert.Equal(today.Task.Title, w.Task.Title);

        foreach (var (c, step) in new[] { (a, "accept"), (b, "accept"), (a, "complete"), (b, "complete") })
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/v1/couple/tasks/{today.Task.Id}/{step}", null)).StatusCode);
        (await b.PostAsJsonAsync($"/v1/couple/questions/{today.Question.Id}/answer", new { text = "Море" })).EnsureSuccessStatusCode();

        w = (await a.GetFromJsonAsync<WidgetDto>("/v1/couple/widget"))!;
        Assert.Equal(TaskState.Done, w.Task.State);
        Assert.Equal(1, w.Task.Done);
        Assert.Equal(1, w.Streak);
        Assert.Equal(1, w.StreakTomorrow);
        Assert.Equal(Nur.TaskCompleted, w.NurEarned);
        Assert.Equal(Nur.TaskCompleted, w.NurBalance);
        Assert.False(w.Question.Answered);
        Assert.True(w.Question.PartnerAnswered);
        Assert.False(w.Question.Revealed);

        // Seen from the partner's side, the names and the question flip.
        var wb = (await b.GetFromJsonAsync<WidgetDto>("/v1/couple/widget"))!;
        Assert.Equal("Алмаз", wb.PartnerName);
        Assert.True(wb.Question.Answered);
        Assert.False(wb.Question.PartnerAnswered);

        api.Clock.Advance(TimeSpan.FromDays(1));
        a = await api.SignInAsync("+77010000001");
        w = (await a.GetFromJsonAsync<WidgetDto>("/v1/couple/widget"))!;
        Assert.Equal(2, w.DaysTogether);
        Assert.Equal(TaskState.Offered, w.Task.State);
        Assert.Equal(1, w.Streak);
    }

    [Fact]
    public async Task Widget_needs_a_couple()
    {
        await using var api = await ApiFactory.StartAsync();
        var solo = await api.SignInAsync("+77010000009", "Соло");
        Assert.Equal(HttpStatusCode.Conflict, (await solo.GetAsync("/v1/couple/widget")).StatusCode);
    }
}
