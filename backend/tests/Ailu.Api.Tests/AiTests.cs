using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Features.Ai;
using Ailu.Api.Features.Daily;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ailu.Api.Tests;

public class AiTests
{
    private static async Task SetPlan(ApiFactory api, CouplePlan plan)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AiluDb>().Couples
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Plan, plan));
    }

    private static Task<HttpResponseMessage> Ask(HttpClient c, string kind, string? budget = null, string? note = null) =>
        c.PostAsJsonAsync("/v1/couple/ai/picks", new { kind, budget, wish = note });

    private static async Task<AiPickDto> Pick(HttpClient c, string kind, string? budget = null, string? note = null)
    {
        var res = await Ask(c, kind, budget, note);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<AiPickDto>())!;
    }

    private static async Task<AiDto> Get(HttpClient c) => (await c.GetFromJsonAsync<AiDto>("/v1/couple/ai"))!;

    private static async Task<string?> CodeOf(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static async Task Finish(HttpClient a, HttpClient b, Guid id)
    {
        foreach (var (c, step) in new[] { (a, "accept"), (b, "accept"), (a, "complete"), (b, "complete") })
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/v1/couple/tasks/{id}/{step}", null)).StatusCode);
    }

    private static async Task<TaskDto> More(HttpClient c)
    {
        var res = await c.PostAsync("/v1/couple/tasks/more", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<TaskDto>())!;
    }

    [Theory]
    [InlineData(CouplePlan.Free)]
    [InlineData(CouplePlan.Plus)]
    public async Task Selections_are_premium_only(CouplePlan plan)
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        await SetPlan(api, plan);

        var ai = await Get(a);
        Assert.False(ai.Available);
        Assert.Equal(0, ai.Limit);
        var res = await Ask(a, "date");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("ai_plan", await CodeOf(res));
        Assert.Empty(api.Ai.Requests);
    }

    [Fact]
    public async Task A_premium_selection_is_five_ideas_both_partners_see_in_their_own_language()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        (await b.PatchAsJsonAsync("/v1/me", new { locale = "kk" })).EnsureSuccessStatusCode();
        // The trial is Premium.
        (await a.PostAsync("/v1/billing/trial", null)).EnsureSuccessStatusCode();

        var pick = await Pick(a, "date", note: "что-то на воздухе");
        Assert.Equal(5, pick.Items.Count);
        Assert.Equal("date", pick.Kind);
        Assert.True(pick.ByMe);
        Assert.All(pick.Items, i => Assert.Contains(StubAiWriter.Catalog[AiPickKind.Date], d => d.Title == i.Title));

        var theirs = await Get(b);
        Assert.True(theirs.Available);
        Assert.Equal((1, Plans.PremiumAiPicksPerMonth), (theirs.Used, theirs.Limit));
        Assert.Equal(new DateOnly(2026, 11, 1), theirs.ResetsOn);
        var seen = Assert.Single(theirs.Picks);
        Assert.False(seen.ByMe);
        Assert.Equal("что-то на воздухе", seen.Wish);
        var first = StubAiWriter.Catalog[AiPickKind.Date].Single(d => d.Title == pick.Items[0].Title);
        Assert.Equal((first.TitleKk, first.BodyKk), (seen.Items[0].Title, seen.Items[0].Body));

        // The next date selection brings ideas the couple hasn't seen yet.
        var next = await Pick(b, "date");
        Assert.Empty(next.Items.Select(i => i.Id).Intersect(pick.Items.Select(i => i.Id)));
        var request = api.Ai.Requests.OfType<IdeaRequest>().Last();
        Assert.Equal(pick.Items.Select(i => i.Title).Order(), request.Avoid.Order());
    }

    [Fact]
    public async Task Budget_and_kind_shape_the_request()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Premium);

        var free = await Pick(a, "home", "free");
        Assert.All(free.Items, i => Assert.Equal(0, i.CostKzt));
        // Films and music have no budget.
        Assert.Equal("any", (await Pick(a, "film", "free")).Budget);

        Assert.Equal("invalid_kind", await CodeOf(await Ask(a, "party")));
        Assert.Equal("invalid_kind", await CodeOf(await Ask(a, "1")));
        Assert.Equal("invalid_budget", await CodeOf(await Ask(a, "date", "huge")));
        Assert.Equal("invalid_wish", await CodeOf(await Ask(a, "date", note: new string('а', 141))));
        Assert.Equal(2, (await Get(a)).Used);
    }

    [Fact]
    public async Task Thirty_selections_a_month_then_the_next_month()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Premium);

        for (var i = 0; i < Plans.PremiumAiPicksPerMonth; i++) await Pick(i % 2 == 0 ? a : b, "music");
        var res = await Ask(a, "date");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("ai_quota", await CodeOf(res));

        // 1 November in Almaty.
        api.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 1, 0, 30, 0, TimeSpan.FromHours(5)).ToUniversalTime());
        a = await api.SignInAsync("+77010000001");
        Assert.Equal(0, (await Get(a)).Used);
        await Pick(a, "date");
    }

    [Fact]
    public async Task A_failed_selection_does_not_count()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Premium);

        api.Ai.Down = true;
        var res = await Ask(a, "date");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Equal("ai_unavailable", await CodeOf(res));
        var ai = await Get(a);
        Assert.Equal(0, ai.Used);
        Assert.Empty(ai.Picks);

        api.Ai.Down = false;
        await Pick(a, "date");
        Assert.Equal(1, (await Get(a)).Used);
    }

    [Fact]
    public async Task Either_partner_saves_an_idea_for_both_and_saved_ideas_outlast_the_plan()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Premium);
        var pick = await Pick(a, "film");
        var film = pick.Items[2];

        var saved = await (await b.PutAsync($"/v1/couple/ai/items/{film.Id}/saved", null)).Content.ReadFromJsonAsync<AiItemDto>();
        Assert.True(saved!.Saved);
        Assert.Equal("film", saved.Kind);
        await SetPlan(api, CouplePlan.Free);

        var ai = await Get(a);
        Assert.False(ai.Available);
        Assert.Equal(film.Id, Assert.Single(ai.Saved).Id);
        Assert.True(ai.Picks.Single().Items.Single(i => i.Id == film.Id).Saved);
        Assert.Equal(pick.Id, (await a.GetFromJsonAsync<AiPickDto>($"/v1/couple/ai/picks/{pick.Id}"))!.Id);

        (await a.DeleteAsync($"/v1/couple/ai/items/{film.Id}/saved")).EnsureSuccessStatusCode();
        Assert.Empty((await Get(b)).Saved);

        // Another couple can't touch it.
        var (c, _) = await api.PairAsync("+77010000003", "+77010000004");
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsync($"/v1/couple/ai/items/{film.Id}/saved", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/v1/couple/ai/picks/{pick.Id}")).StatusCode);
    }

    [Fact]
    public async Task Premium_extra_tasks_are_personal_and_stay_with_the_couple()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        (await b.PatchAsJsonAsync("/v1/me", new { locale = "kk" })).EnsureSuccessStatusCode();
        await SetPlan(api, CouplePlan.Premium);

        var today = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        Assert.False(today.Task.Personal);
        await Finish(a, b, today.Task.Id);

        var second = await More(a);
        Assert.True(second.Personal);
        Assert.Equal(TaskState.Offered, second.State);
        // The partner asking right after gets the same task.
        var theirs = await More(b);
        Assert.Equal(second.Id, theirs.Id);
        Assert.NotEqual(second.Title, theirs.Title);
        await Finish(a, b, second.Id);
        var third = await More(b);
        Assert.True(third.Personal);
        Assert.NotEqual(second.Title, third.Title);

        // The model saw the tasks to avoid and no one's name.
        var request = api.Ai.Requests.OfType<TaskRequest>().Last();
        Assert.Contains(second.Title, request.Avoid);
        Assert.Contains(today.Task.Category, request.Categories);
        Assert.DoesNotContain("Алмаз", AiPrompts.TaskUser(request));

        // Personal tasks never reach the library, so another couple's day doesn't get them.
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
        Assert.Equal(Seed.Tasks.Length, await db.TaskLibrary.CountAsync());
        Assert.Equal(2, await db.DailyTasks.CountAsync(t => t.CoupleId != null));
    }

    [Fact]
    public async Task Without_the_model_premium_gets_a_library_task()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Premium);
        var today = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        await Finish(a, b, today.Task.Id);

        api.Ai.Down = true;
        var second = await More(a);
        Assert.False(second.Personal);
        Assert.Contains(Seed.Tasks, t => t.Slug == second.Slug);
    }

    [Fact]
    public async Task Plus_extra_tasks_come_from_the_library()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Plus);
        var today = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        await Finish(a, b, today.Task.Id);
        Assert.False((await More(a)).Personal);
        Assert.Empty(api.Ai.Requests);
    }

    [Fact]
    public async Task The_model_hears_about_the_couple_but_not_who_they_are()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Premium);
        string[] Leaning(string l) => LoveQuiz.Questions.Select(q => q.Contains(l) ? l : q[0]).ToArray();
        (await a.PostAsJsonAsync("/v1/couple/love-quiz/answers", new { answers = Leaning(LoveQuiz.Time) })).EnsureSuccessStatusCode();
        (await b.PostAsJsonAsync("/v1/couple/love-quiz/answers", new { answers = Leaning(LoveQuiz.Words) })).EnsureSuccessStatusCode();
        var today = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        await Finish(a, b, today.Task.Id);
        (await a.PostAsJsonAsync($"/v1/couple/questions/{today.Question.Id}/answer", new { text = "Секретный ответ Алмаза" })).EnsureSuccessStatusCode();

        await Pick(a, "date", "low", "без кино");
        var request = api.Ai.Requests.OfType<IdeaRequest>().Single();
        var prompt = AiPrompts.IdeasUser(request);

        Assert.Equal([[LoveQuiz.Time], [LoveQuiz.Words]], new[] { request.Couple.LanguagesA, request.Couple.LanguagesB }.OrderBy(l => l[0] == LoveQuiz.Words));
        Assert.Contains(Seed.Tasks.Single(t => t.Slug == today.Task.Slug).Title, request.Couple.RecentTasks);
        Assert.Contains("quality time", prompt);
        Assert.Contains("at most 5 000 ₸", prompt);
        Assert.Contains("<wish>без кино</wish>", prompt);
        Assert.Contains("autumn", prompt);
        foreach (var secret in new[] { "Алмаз", "Нурай", "+7701", "Секретный" }) Assert.DoesNotContain(secret, prompt);
    }

    [Fact]
    public async Task Deleting_an_account_removes_the_wishes_they_typed()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Premium);
        await Pick(a, "home", note: "хочу тишины");
        await Pick(b, "home", note: "и пиццу");

        (await a.DeleteAsync("/v1/me")).EnsureSuccessStatusCode();
        using var scope = api.Services.CreateScope();
        var notes = await scope.ServiceProvider.GetRequiredService<AiluDb>().AiPicks.Select(p => p.Note).ToListAsync();
        Assert.Equal([null, "и пиццу"], notes.OrderBy(n => n));
    }

    [Fact]
    public void What_the_model_writes_is_checked_and_cut_to_size()
    {
        var json = JsonDocument.Parse($$"""
            {"ideas": [
              {"title": "Каток", "body": "Покатайтесь.", "title_kk": "Мұз айдыны", "body_kk": "Коньки тебіңіз.", "cost_kzt": -5, "duration_min": 99999},
              {"title": "Без перевода", "body": "Текст.", "title_kk": "", "body_kk": "", "cost_kzt": 0, "duration_min": 10},
              {"title": "{{new string('я', 200)}}", "body": "Длинно.", "title_kk": "Ұзын", "body_kk": "Ұзақ.", "cost_kzt": 3000, "duration_min": 60}
            ]}
            """).RootElement;
        var ideas = AiPrompts.ParseIdeas(json);
        Assert.Equal(2, ideas.Count);
        Assert.Equal((0, 1440), (ideas[0].CostKzt, ideas[0].DurationMin));
        Assert.Equal(120, ideas[1].Title.Length);
        Assert.Empty(AiPrompts.ParseIdeas(JsonDocument.Parse("""{"oops": 1}""").RootElement));

        var task = AiPrompts.ParseTask(JsonDocument.Parse("""
            {"category": "made-up", "title": "Т", "body": "Б", "title_kk": "Т", "body_kk": "Б", "cost_kzt": 0, "duration_min": 30}
            """).RootElement, ["dates", "words"]);
        Assert.Equal("dates", task!.Category);
    }

    [Fact]
    public void The_model_request_asks_for_json_with_refusal_fallbacks()
    {
        var request = ClaudeAiWriter.Request(new AiOptions { ApiKey = "k" }, AiPrompts.IdeasSystem, "user", AiPrompts.IdeasSchema);
        var json = JsonSerializer.Serialize(request);
        Assert.Contains("claude-opus-5-5", json);
        Assert.Contains("\"fallbacks\":\"default\"", json);
        Assert.Contains("json_schema", json);
        Assert.Contains("\"effort\":\"low\"", json);
    }
}
