using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ailu.Api.Data;
using Ailu.Api.Features.Daily;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ailu.Api.Tests;

public class StreakRestoreTests
{
    [Fact]
    public async Task Restore_spends_nur_and_the_month_allowance()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();

        async Task NextDay(bool both)
        {
            if (both)
            {
                var t = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
                await a.PostAsync($"/v1/couple/tasks/{t.Task.Id}/accept", null);
                await b.PostAsync($"/v1/couple/tasks/{t.Task.Id}/accept", null);
            }
            api.Clock.Advance(TimeSpan.FromDays(1));
            (a, b) = (await api.SignInAsync("+77010000001"), await api.SignInAsync("+77010000002"));
        }

        await NextDay(true);
        await NextDay(true);
        await NextDay(true);
        await NextDay(false);
        await NextDay(false); // missed two nights: the first is forgiven, the second breaks it

        var streak = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Streak;
        Assert.Equal(0, streak.Days);
        Assert.Equal(new StreakRestoreDto(3, 1, 100, 1, 1, false), streak.Restore);
        await AssertConflict(await a.PostAsync("/v1/couple/streak/restore", null), "not_enough_nur");

        await SetBalance(api, 250);
        var restored = await a.PostAsync("/v1/couple/streak/restore", null);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(new StreakDto(3, new DateOnly(2026, 10, 5), null), await restored.Content.ReadFromJsonAsync<StreakDto>());
        await AssertConflict(await b.PostAsync("/v1/couple/streak/restore", null), "nothing_to_restore");

        var phase = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Phase;
        Assert.Equal(150, phase.Balance);
        Assert.Equal(0, phase.Earned); // spending never lowers the phase

        // Today counts on top of the restored streak. Then one more missed night breaks it, since this
        // week's free night is used up, and Free has only one restore a month.
        await NextDay(true);
        await NextDay(false);
        streak = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Streak;
        Assert.Equal(0, streak.Restore!.RestoresLeft);
        await AssertConflict(await a.PostAsync("/v1/couple/streak/restore", null), "no_restores_left");
    }

    [Fact]
    public async Task Streak_lost_on_starting_over_comes_back_with_the_new_days()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await SetBalance(api, 100);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
            // 47 days up to 27 September, the forgiveness used on 26 September; today is 1 October.
            await db.Couples.ExecuteUpdateAsync(s => s
                .SetProperty(c => c.StreakDays, 47)
                .SetProperty(c => c.StreakLastDay, new DateOnly(2026, 9, 28))
                .SetProperty(c => c.StreakForgivenOn, new DateOnly(2026, 9, 26)));
        }

        var t = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        await a.PostAsync($"/v1/couple/tasks/{t.Task.Id}/accept", null);
        await b.PostAsync($"/v1/couple/tasks/{t.Task.Id}/accept", null);

        var streak = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Streak;
        Assert.Equal(1, streak.Days);
        // Two missed days (29 and 30 September) are more than Free's one restore a month, so none is offered.
        Assert.Null(streak.Restore);
        await AssertConflict(await a.PostAsync("/v1/couple/streak/restore", null), "no_restores_left");

        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AiluDb>().Couples
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Plan, CouplePlan.Plus).SetProperty(c => c.NurBalance, 200));
        streak = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Streak;
        Assert.Equal(new StreakRestoreDto(47, 2, 200, 3, 3, true), streak.Restore);
        var res = await a.PostAsync("/v1/couple/streak/restore", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(48, (await res.Content.ReadFromJsonAsync<StreakDto>())!.Days);

        using (var scope = api.Services.CreateScope())
        {
            var ledger = await scope.ServiceProvider.GetRequiredService<AiluDb>().NurLedger
                .Where(e => e.SourceType == NurSource.StreakRestore).OrderBy(e => e.SourceId).ToListAsync();
            Assert.Equal(["2026-09-29", "2026-09-30"], ledger.Select(e => e.SourceId));
            Assert.All(ledger, e => Assert.Equal(-100, e.Amount));
        }
    }

    private static async Task SetBalance(ApiFactory api, int nur)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AiluDb>().Couples.ExecuteUpdateAsync(s => s.SetProperty(c => c.NurBalance, nur));
    }

    private static async Task AssertConflict(HttpResponseMessage res, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
    }
}
