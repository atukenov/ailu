using System.Net;
using System.Net.Http.Json;
using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Features.LoveQuiz;
using Ailu.Api.Features.Pairing;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ailu.Api.Tests;

public class LoveQuizTests
{
    // Picks `language` wherever it is offered, the first option elsewhere.
    private static string[] Leaning(string language) =>
        LoveQuiz.Questions.Select(q => q.Contains(language) ? language : q[0]).ToArray();

    private static async Task SetPlan(ApiFactory api, CouplePlan plan)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AiluDb>().Couples
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Plan, plan));
    }

    private static async Task<HttpResponseMessage> Submit(HttpClient client, string[] answers) =>
        await client.PostAsJsonAsync("/v1/couple/love-quiz/answers", new { answers });

    private static async Task<LoveQuizDto> Answer(HttpClient client, string[] answers)
    {
        var res = await Submit(client, answers);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<LoveQuizDto>())!;
    }

    private static async Task<LoveQuizDto> Get(HttpClient client) =>
        (await client.GetFromJsonAsync<LoveQuizDto>("/v1/couple/love-quiz"))!;

    private static async Task<int> Earned(HttpClient client) =>
        (await client.GetFromJsonAsync<PhaseDto>("/v1/couple/phase"))!.Earned;

    [Fact]
    public void Every_question_offers_four_languages_and_each_language_comes_up_evenly()
    {
        Assert.Equal(12, LoveQuiz.Questions.Count);
        Assert.All(LoveQuiz.Questions, q => Assert.Equal(4, q.Distinct().Count()));
        foreach (var language in LoveQuiz.Languages)
            Assert.InRange(LoveQuiz.Questions.Count(q => q.Contains(language)), 9, 10);
        // Options move around, so no language is always first.
        Assert.True(LoveQuiz.Questions.Select(q => q[0]).Distinct().Count() > 2);
        // Question 4 is the mockup's "обычный вторник": time, words, service, gifts.
        Assert.Equal(["gifts", "service", "time", "words"], LoveQuiz.Questions[3].Order());
    }

    [Fact]
    public void Scores_are_shares_of_the_times_a_language_was_offered()
    {
        var scores = LoveQuiz.Scores(Leaning(LoveQuiz.Time));
        Assert.Equal(100, scores[LoveQuiz.Time]);
        Assert.Equal([LoveQuiz.Time], LoveQuiz.Top(scores));
        Assert.Equal(new DateOnly(2026, 10, 1), LoveQuiz.QuarterStart(new DateOnly(2026, 12, 31)));
        Assert.Equal(new DateOnly(2027, 1, 1), LoveQuiz.QuarterStart(new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public async Task Free_couples_see_the_quiz_but_need_Plus_to_take_it()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();

        var quiz = await Get(a);
        Assert.False(quiz.Available);
        Assert.Equal(12, quiz.Questions.Count);
        var res = await Submit(a, Leaning(LoveQuiz.Words));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("quiz_plan", await res.Content.ReadAsStringAsync());

        (await a.PostAsync("/v1/billing/trial", null)).EnsureSuccessStatusCode();
        Assert.True((await Get(a)).Available);
    }

    [Fact]
    public async Task Result_opens_only_when_both_have_answered_and_earns_nur()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Plus);
        var before = await Earned(a);

        var mine = await Answer(a, Leaning(LoveQuiz.Time));
        Assert.Equal(new LoveQuizRoundDto(true, false), mine.Round);
        Assert.Null(mine.Result);

        // The partner learns that it was taken, never what was picked.
        var theirs = await Get(b);
        Assert.Equal(new LoveQuizRoundDto(false, true), theirs.Round);
        Assert.Null(theirs.Result);
        Assert.DoesNotContain("\"scores\"", await b.GetStringAsync("/v1/couple/love-quiz"));

        var done = await Answer(b, Leaning(LoveQuiz.Words));
        Assert.Null(done.Round);
        Assert.Equal([LoveQuiz.Words], done.Result!.Me!.Top);
        Assert.Equal([LoveQuiz.Time], done.Result.Partner!.Top);
        Assert.Equal(Nur.LoveQuizByBoth, done.Result.NurAwarded);
        Assert.Equal(before + Nur.LoveQuizByBoth, await Earned(a));

        var seenByA = (await Get(a)).Result!;
        Assert.Equal([LoveQuiz.Time], seenByA.Me!.Top);
        Assert.Equal([LoveQuiz.Words], seenByA.Partner!.Top);
    }

    [Fact]
    public async Task Answers_must_cover_every_question_once()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Plus);

        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(a, Leaning(LoveQuiz.Time)[..11])).StatusCode);
        var offQuestion = Leaning(LoveQuiz.Time);
        offQuestion[0] = LoveQuiz.Languages.Single(l => !LoveQuiz.Questions[0].Contains(l));
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(a, offQuestion)).StatusCode);

        await Answer(a, Leaning(LoveQuiz.Time));
        var again = await Submit(a, Leaning(LoveQuiz.Gifts));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("quiz_answered", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Retaking_keeps_the_last_result_and_earns_nur_once_a_quarter()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Plus);
        await Answer(a, Leaning(LoveQuiz.Time));
        await Answer(b, Leaning(LoveQuiz.Words));
        var earned = await Earned(a);

        // A new round: the earlier result stays until both have answered again.
        api.Clock.Advance(TimeSpan.FromMinutes(5));
        var retake = await Answer(a, Leaning(LoveQuiz.Touch));
        Assert.Equal([LoveQuiz.Time], retake.Result!.Me!.Top);
        var second = await Answer(b, Leaning(LoveQuiz.Gifts));
        Assert.Equal([LoveQuiz.Gifts], second.Result!.Me!.Top);
        Assert.Equal(0, second.Result.NurAwarded);
        Assert.Equal(earned, await Earned(a));

        // The next quarter (October 1 to January 1) pays again.
        api.Clock.Advance(TimeSpan.FromDays(92));
        a = await api.SignInAsync("+77010000001");
        b = await api.SignInAsync("+77010000002");
        await Answer(a, Leaning(LoveQuiz.Service));
        Assert.Equal(Nur.LoveQuizByBoth, (await Answer(b, Leaning(LoveQuiz.Service))).Result!.NurAwarded);
        Assert.Equal(earned + Nur.LoveQuizByBoth, await Earned(a));
    }

    [Fact]
    public async Task Tasks_lean_towards_the_couples_love_languages()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Plus);
        await Answer(a, Leaning(LoveQuiz.Time));
        await Answer(b, Leaning(LoveQuiz.Time));

        // Time together favours walks, dinners, sunsets and dates: the first four days are all of them.
        for (var day = 0; day < 4; day++)
        {
            if (day > 0)
            {
                api.Clock.Advance(TimeSpan.FromDays(1));
                a = await api.SignInAsync("+77010000001");
            }
            (await a.GetAsync("/v1/couple/today")).EnsureSuccessStatusCode();
        }

        using var scope = api.Services.CreateScope();
        var categories = await scope.ServiceProvider.GetRequiredService<AiluDb>().CoupleTasks
            .Select(t => t.DailyTask!.Category).ToListAsync();
        Assert.Equal(4, categories.Count);
        Assert.All(categories, c => Assert.Contains(c, new[] { "time-together", "dates" }));
    }
}

public class LoveQuizPickTests
{
    [Fact]
    public void Preferred_items_may_run_a_round_ahead()
    {
        var library = new[] { 1, 2, 3, 4 };
        var preferred = new HashSet<int> { 4 };
        var couple = Guid.NewGuid();
        var used = new List<int>();
        for (var i = 0; i < 6; i++)
            used.Add(DailyContentService.PickLeastUsed(library, used, couple, new DateOnly(2026, 1, 1).AddDays(i), preferred));
        // Item 4 goes first and comes up again before the others' second turn, never twice in a row.
        Assert.Equal(4, used[0]);
        Assert.Equal([1, 2, 3], used[1..4].Order());
        Assert.Equal(4, used[4]);
        Assert.NotEqual(4, used[5]);
        Assert.All(new[] { 1, 2, 3 }, x => Assert.Contains(x, used));
    }
}
