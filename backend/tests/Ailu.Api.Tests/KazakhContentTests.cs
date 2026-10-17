using System.Net.Http.Json;
using Ailu.Api.Data;
using Ailu.Api.Features.Daily;
using Ailu.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Ailu.Api.Tests;

public class KazakhContentTests
{
    [Fact]
    public void Every_task_and_question_has_kazakh_text()
    {
        Assert.All(Seed.Tasks, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.TitleKk), t.Slug);
            Assert.False(string.IsNullOrWhiteSpace(t.BodyKk), t.Slug);
        });
        Assert.All(Seed.Questions, q => Assert.False(string.IsNullOrWhiteSpace(q.TextKk), q.Slug));
    }

    [Fact]
    public void Missing_kazakh_text_falls_back_to_russian()
    {
        var q = new Question { Slug = "new", Text = "Новый вопрос?", TextKk = "" };
        Assert.Equal("Новый вопрос?", q.TextIn("kk"));
        Assert.Equal("Новый вопрос?", q.TextIn("en"));
    }

    // Partners can choose different languages: each reads the same task and question in their own.
    [Fact]
    public async Task Each_partner_reads_the_day_in_their_own_language()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        (await a.PatchAsJsonAsync("/v1/me", new { locale = "kk" })).EnsureSuccessStatusCode();

        var kk = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        var ru = (await b.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        var task = Seed.Tasks.Single(t => t.Slug == kk.Task.Slug);
        var question = Seed.Questions.Single(q => q.Text == ru.Question.Text);

        Assert.Equal(kk.Task.Id, ru.Task.Id);
        Assert.Equal((task.TitleKk, task.BodyKk), (kk.Task.Title, kk.Task.Body));
        Assert.Equal((task.Title, task.Body), (ru.Task.Title, ru.Task.Body));
        Assert.Equal(question.TextKk, kk.Question.Text);
    }

    [Fact]
    public async Task Morning_push_speaks_each_partners_language()
    {
        await using var api = await ApiFactory.StartAsync();
        api.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.FromHours(5)).ToUniversalTime());
        var (a, b) = await api.PairAsync();
        (await a.PatchAsJsonAsync("/v1/me", new { locale = "kk" })).EnsureSuccessStatusCode();
        (await a.PutAsJsonAsync("/v1/me/devices", new { pushToken = "ExponentPushToken[a]", platform = "ios" })).EnsureSuccessStatusCode();
        (await b.PutAsJsonAsync("/v1/me/devices", new { pushToken = "ExponentPushToken[b]", platform = "android" })).EnsureSuccessStatusCode();
        var slug = (await b.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Task.Slug;
        var task = Seed.Tasks.Single(t => t.Slug == slug);

        api.Clock.Advance(TimeSpan.FromHours(1));
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DailyPushJob>().RunOnceAsync();

        var toA = api.Push.Sent.Single(p => p.To == "ExponentPushToken[a]");
        var toB = api.Push.Sent.Single(p => p.To == "ExponentPushToken[b]");
        Assert.Equal(("Бүгінгі тапсырма", task.TitleKk), (toA.Title, toA.Body));
        Assert.Equal(("Задание на сегодня", task.Title), (toB.Title, toB.Body));
    }
}
