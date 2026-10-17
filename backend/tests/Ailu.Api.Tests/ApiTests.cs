using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ailu.Api.Data;
using Ailu.Api.Features.Auth;
using Ailu.Api.Features.Daily;
using Ailu.Api.Features.Pairing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ailu.Api.Tests;

public class AuthTests
{
    [Fact]
    public async Task Phone_code_signs_in_and_creates_user_once()
    {
        await using var api = await ApiFactory.StartAsync();
        var a = await api.SignInAsync("8 701 000 00 01");
        var me = await a.GetFromJsonAsync<UserDto>("/v1/me");
        Assert.Equal("+77010000001", me!.Phone);

        api.Clock.Advance(TimeSpan.FromMinutes(2));
        var again = await api.SignInAsync("+77010000001");
        Assert.Equal(me.Id, (await again.GetFromJsonAsync<UserDto>("/v1/me"))!.Id);
    }

    [Fact]
    public async Task Resend_is_throttled_and_wrong_codes_run_out()
    {
        await using var api = await ApiFactory.StartAsync();
        var c = api.CreateClient();
        var phone = "+77010000001";
        Assert.Equal(HttpStatusCode.Accepted, (await c.PostAsJsonAsync("/v1/auth/phone/request", new { phone })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.PostAsJsonAsync("/v1/auth/phone/request", new { phone })).StatusCode);

        var real = api.Sms.LastCode(phone);
        var wrong = real == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/v1/auth/phone/verify", new { phone, code = wrong })).StatusCode);
        // After five misses even the right code is refused.
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/v1/auth/phone/verify", new { phone, code = real })).StatusCode);
    }

    // The app translates problems by `code`, so each sign-in failure needs its own.
    [Fact]
    public async Task Sign_in_problems_carry_codes_the_app_can_translate()
    {
        await using var api = await ApiFactory.StartAsync();
        var c = api.CreateClient();
        Assert.Equal("invalid_phone", await CodeOf(await c.PostAsJsonAsync("/v1/auth/phone/request", new { phone = "123" })));

        var phone = "+77010000001";
        await c.PostAsJsonAsync("/v1/auth/phone/request", new { phone });
        var wrong = api.Sms.LastCode(phone) == "000000" ? "111111" : "000000";
        Assert.Equal("wrong_code", await CodeOf(await c.PostAsJsonAsync("/v1/auth/phone/verify", new { phone, code = wrong })));
        Assert.Equal("invalid_code", await CodeOf(await c.PostAsJsonAsync("/v1/auth/phone/verify", new { phone, code = " " })));
        Assert.Equal("session_expired", await CodeOf(await c.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = "nope" })));
    }

    private static async Task<string?> CodeOf(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Refresh_token_rotates_and_works_once()
    {
        await using var api = await ApiFactory.StartAsync();
        var c = api.CreateClient();
        var phone = "+77010000001";
        await c.PostAsJsonAsync("/v1/auth/phone/request", new { phone });
        var pair = await (await c.PostAsJsonAsync("/v1/auth/phone/verify", new { phone, code = api.Sms.LastCode(phone) }))
            .Content.ReadFromJsonAsync<TokenPair>();

        var first = await c.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = pair!.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await c.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = pair.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    public async Task Access_token_expires()
    {
        await using var api = await ApiFactory.StartAsync();
        var a = await api.SignInAsync("+77010000001");
        api.Clock.Advance(TimeSpan.FromMinutes(62));
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/v1/me")).StatusCode);
    }
}

public class PairingTests
{
    [Fact]
    public async Task Invitation_creates_couple_for_the_invited_number_only()
    {
        await using var api = await ApiFactory.StartAsync();
        var a = await api.SignInAsync("+77010000001", "Алмаз");
        var b = await api.SignInAsync("+77010000002", "Нурай");
        var stranger = await api.SignInAsync("+77010000003");

        (await a.PostAsJsonAsync("/v1/couple/invitations", new { phone = "+77010000002" })).EnsureSuccessStatusCode();
        var code = api.Sms.LastCode("+77010000002");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/v1/couple/invitations/{code}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.PostAsync($"/v1/couple/invitations/{code}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/v1/couple/invitations/{code}/accept", null)).StatusCode);

        var fromA = await a.GetFromJsonAsync<CoupleDto>("/v1/couple");
        var fromB = await b.GetFromJsonAsync<CoupleDto>("/v1/couple");
        Assert.Equal(fromA!.Id, fromB!.Id);
        Assert.Equal("Нурай", fromA.Partner!.Name);
        Assert.Equal("Алмаз", fromB.Partner!.Name);
        Assert.Equal(HttpStatusCode.Conflict, (await stranger.GetAsync("/v1/couple")).StatusCode);
    }

    [Fact]
    public async Task Invitation_expires_after_a_day()
    {
        await using var api = await ApiFactory.StartAsync();
        var a = await api.SignInAsync("+77010000001");
        var b = await api.SignInAsync("+77010000002");
        await a.PostAsJsonAsync("/v1/couple/invitations", new { phone = "+77010000002" });
        api.Clock.Advance(TimeSpan.FromHours(25));
        // Tokens have expired too; sign in again to isolate the invitation expiry.
        b = await api.SignInAsync("+77010000002");
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/v1/couple/invitations/{api.Sms.LastCode("+77010000002")}/accept", null)).StatusCode);
    }

    [Fact]
    public async Task Days_together_counts_in_the_couples_time_zone()
    {
        await using var api = await ApiFactory.StartAsync();
        // 20:00 UTC is already 01:00 the next day in Almaty; pairing then is still day one.
        api.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero));
        var (a, _) = await api.PairAsync();
        Assert.Equal(1, (await a.GetFromJsonAsync<CoupleDto>("/v1/couple"))!.DaysTogether);
    }

    [Fact]
    public async Task Cannot_invite_while_in_a_couple()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync("/v1/couple/invitations", new { phone = "+77010000009" })).StatusCode);
    }

    [Fact]
    public async Task Ending_a_couple_keeps_its_history()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/v1/couple")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.GetAsync("/v1/couple")).StatusCode);

        using var scope = api.Services.CreateScope();
        var couple = await scope.ServiceProvider.GetRequiredService<AiluDb>().Couples.Include(c => c.Members).SingleAsync();
        Assert.Equal(CoupleStatus.Ended, couple.Status);
        Assert.Equal(2, couple.Members.Count);
        Assert.All(couple.Members, m => Assert.NotNull(m.LeftAt));
    }
}

public class DailyLoopTests
{
    [Fact]
    public async Task Both_partners_see_the_same_task_and_question()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var ta = await a.GetFromJsonAsync<TodayDto>("/v1/couple/today");
        var tb = await b.GetFromJsonAsync<TodayDto>("/v1/couple/today");
        Assert.Equal(ta!.Task.Id, tb!.Task.Id);
        Assert.Equal(ta.Question.Id, tb.Question.Id);
        Assert.Equal(new DateOnly(2026, 10, 1), ta.Date);
        Assert.Equal(TaskState.Offered, ta.Task.State);
    }

    [Fact]
    public async Task Task_needs_both_to_accept_and_the_other_to_confirm()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var id = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Task.Id;

        var accepted = await Post<TaskDto>(a, $"/v1/couple/tasks/{id}/accept");
        Assert.Equal(TaskState.WaitingForPartner, accepted.State);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsync($"/v1/couple/tasks/{id}/complete", null)).StatusCode);

        Assert.Equal(TaskState.Accepted, (await Post<TaskDto>(b, $"/v1/couple/tasks/{id}/accept")).State);
        Assert.Equal(TaskState.WaitingForConfirmation, (await Post<TaskDto>(a, $"/v1/couple/tasks/{id}/complete")).State);
        // Marking it again yourself does not confirm it.
        Assert.Equal(TaskState.WaitingForConfirmation, (await Post<TaskDto>(a, $"/v1/couple/tasks/{id}/complete")).State);
        Assert.Equal(0, (await a.GetFromJsonAsync<PhaseDto>("/v1/couple/phase"))!.Earned);

        Assert.Equal(TaskState.Done, (await Post<TaskDto>(b, $"/v1/couple/tasks/{id}/complete")).State);
        Assert.Equal(TaskState.Done, (await Post<TaskDto>(b, $"/v1/couple/tasks/{id}/complete")).State);
        Assert.Equal(50, (await a.GetFromJsonAsync<PhaseDto>("/v1/couple/phase"))!.Earned);
    }

    [Fact]
    public async Task Partner_answer_stays_off_the_device_until_both_answer()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var qid = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Question.Id;

        var mine = await Post<QuestionDto>(a, $"/v1/couple/questions/{qid}/answer", new { text = "Лето у бабушки" });
        Assert.Equal("Лето у бабушки", mine.MyAnswer);
        Assert.False(mine.PartnerAnswered);

        // B can see that A answered, but not what. Check the raw JSON, not just the model.
        var raw = await b.GetStringAsync("/v1/couple/today");
        Assert.DoesNotContain("Лето у бабушки", raw);
        var question = JsonDocument.Parse(raw).RootElement.GetProperty("question");
        Assert.True(question.GetProperty("partnerAnswered").GetBoolean());

        // Before the reveal an answer can still be changed.
        await Post<QuestionDto>(a, $"/v1/couple/questions/{qid}/answer", new { text = "Лето у бабушки в Шымкенте" });

        var revealed = await Post<QuestionDto>(b, $"/v1/couple/questions/{qid}/answer", new { text = "Первый велосипед" });
        Assert.Equal("Лето у бабушки в Шымкенте", revealed.PartnerAnswer);
        Assert.NotNull(revealed.RevealedAt);
        Assert.Equal("Первый велосипед", (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Question.PartnerAnswer);

        var locked = await a.PostAsJsonAsync($"/v1/couple/questions/{qid}/answer", new { text = "Передумал" });
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Equal(30, (await a.GetFromJsonAsync<PhaseDto>("/v1/couple/phase"))!.Earned);
    }

    [Fact]
    public async Task Another_couples_items_are_invisible()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var (c, _) = await api.PairAsync("+77010000003", "+77010000004");
        var theirs = (await c.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;

        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync($"/v1/couple/tasks/{theirs.Task.Id}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await a.PostAsJsonAsync($"/v1/couple/questions/{theirs.Question.Id}/answer", new { text = "x" })).StatusCode);
    }

    [Fact]
    public async Task Yesterdays_task_is_closed()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var first = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;

        api.Clock.Advance(TimeSpan.FromDays(1));
        (a, b) = (await api.SignInAsync("+77010000001"), await api.SignInAsync("+77010000002"));
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync($"/v1/couple/tasks/{first.Task.Id}/accept", null)).StatusCode);

        var second = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        Assert.NotEqual(first.Task.Id, second.Task.Id);
        Assert.NotEqual(first.Task.Slug, second.Task.Slug);
    }

    [Fact]
    public async Task Streak_counts_days_when_both_were_active()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();

        async Task<int> Day(bool both)
        {
            var t = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
            await a.PostAsync($"/v1/couple/tasks/{t.Task.Id}/accept", null);
            if (both) await b.PostAsJsonAsync($"/v1/couple/questions/{t.Question.Id}/answer", new { text = "да" });
            var streak = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Streak.Days;
            api.Clock.Advance(TimeSpan.FromDays(1));
            (a, b) = (await api.SignInAsync("+77010000001"), await api.SignInAsync("+77010000002"));
            return streak;
        }

        Assert.Equal(1, await Day(both: true));
        Assert.Equal(2, await Day(both: true));
        Assert.Equal(2, await Day(both: false)); // only one partner: day not counted, streak not broken yet
        Assert.Equal(3, await Day(both: true));  // the missed night is forgiven
        Assert.Equal(3, await Day(both: false));
        Assert.Equal(1, await Day(both: true));  // second miss in a week starts over
    }

    private static async Task<T> Post<T>(HttpClient c, string url, object? body = null)
    {
        var res = body is null ? await c.PostAsync(url, null) : await c.PostAsJsonAsync(url, body);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }
}
