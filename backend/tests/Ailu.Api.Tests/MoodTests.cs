using System.Net;
using System.Net.Http.Json;
using Ailu.Api.Data;
using Ailu.Api.Features.Mood;
using Ailu.Api.Features.Pairing;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ailu.Api.Tests;

public class MoodTests
{
    private static async Task<(HttpClient A, HttpClient B)> ConsentedPair(ApiFactory api)
    {
        var (a, b) = await api.PairAsync();
        (await a.PutAsync("/v1/me/mood-consent", null)).EnsureSuccessStatusCode();
        (await b.PutAsync("/v1/me/mood-consent", null)).EnsureSuccessStatusCode();
        return (a, b);
    }

    // Moving the clock a day expires the access tokens, so sign in again after.
    private static async Task<(HttpClient A, HttpClient B)> NextDays(ApiFactory api, int days)
    {
        api.Clock.Advance(TimeSpan.FromDays(days));
        return (await api.SignInAsync("+77010000001"), await api.SignInAsync("+77010000002"));
    }

    private static async Task<MoodDto> Set(HttpClient client, string mood, string? note = null)
    {
        var res = await client.PutAsJsonAsync("/v1/couple/mood", new { mood, note });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<MoodDto>())!;
    }

    private static async Task<MoodDayDto> Today(HttpClient client) =>
        (await client.GetFromJsonAsync<MoodDto>("/v1/couple/mood"))!.Days[^1];

    private static async Task<int> Earned(HttpClient client) =>
        (await client.GetFromJsonAsync<PhaseDto>("/v1/couple/phase"))!.Earned;

    [Fact]
    public async Task Mood_needs_explicit_consent()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();

        Assert.False((await a.GetFromJsonAsync<MoodDto>("/v1/couple/mood"))!.Consented);
        var res = await a.PutAsJsonAsync("/v1/couple/mood", new { mood = "good" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("consent_required", await res.Content.ReadAsStringAsync());

        (await a.PutAsync("/v1/me/mood-consent", null)).EnsureSuccessStatusCode();
        var mood = await Set(a, "good");
        Assert.True(mood.Consented);
        Assert.Equal("good", mood.Days[^1].Mine!.Mood);
    }

    [Fact]
    public async Task Partner_sees_the_mood_but_not_the_note_until_it_is_shared()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await ConsentedPair(api);

        await Set(a, "tired", "Длинный день на работе");
        var seen = (await Today(b)).Partner!;
        Assert.Equal("tired", seen.Mood);
        Assert.Null(seen.Note);
        Assert.DoesNotContain("Длинный", await b.GetStringAsync("/v1/couple/mood"));

        (await a.PutAsync("/v1/couple/mood/note-shared", null)).EnsureSuccessStatusCode();
        Assert.Equal("Длинный день на работе", (await Today(b)).Partner!.Note);

        // A new note is private again, even though the old one was shared.
        var mine = (await Set(a, "even", "Уже лучше")).Days[^1].Mine!;
        Assert.False(mine.NoteShared);
        Assert.Null((await Today(b)).Partner!.Note);

        // Changing only the mood keeps the note and its sharing as they were.
        (await a.PutAsync("/v1/couple/mood/note-shared", null)).EnsureSuccessStatusCode();
        var kept = (await Set(a, "good")).Days[^1].Mine!;
        Assert.Equal("Уже лучше", kept.Note);
        Assert.True(kept.NoteShared);

        (await a.DeleteAsync("/v1/couple/mood/note-shared")).EnsureSuccessStatusCode();
        Assert.Null((await Today(b)).Partner!.Note);
    }

    [Fact]
    public async Task Both_marking_the_same_day_earns_ten_nur_once()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await ConsentedPair(api);

        await Set(a, "good");
        Assert.Equal(0, await Earned(a));
        await Set(b, "great");
        Assert.Equal(10, await Earned(a));

        await Set(a, "even");
        await Set(b, "good");
        Assert.Equal(10, await Earned(a));

        (a, b) = await NextDays(api, 1);
        await Set(b, "good");
        await Set(a, "good");
        Assert.Equal(20, await Earned(a));

        // Both marking counts as a shared action for the streak.
        var today = await a.GetFromJsonAsync<Ailu.Api.Features.Daily.TodayDto>("/v1/couple/today");
        Assert.Equal(2, today!.Streak.Days);
    }

    [Fact]
    public async Task Mood_and_note_are_validated()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await ConsentedPair(api);

        Assert.Equal(HttpStatusCode.BadRequest, (await a.PutAsJsonAsync("/v1/couple/mood", new { mood = "ecstatic" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PutAsJsonAsync("/v1/couple/mood", new { mood = "3" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await a.PutAsJsonAsync("/v1/couple/mood", new { mood = "good", note = new string('а', 281) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PutAsync("/v1/couple/mood/note-shared", null)).StatusCode);
    }

    [Fact]
    public async Task Week_shows_both_partners_by_day()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await ConsentedPair(api);

        await Set(a, "hard");
        (a, b) = await NextDays(api, 2);
        await Set(b, "great");

        var mood = (await a.GetFromJsonAsync<MoodDto>("/v1/couple/mood"))!;
        Assert.Equal(MoodEndpoints.HistoryDays, mood.Days.Count);
        Assert.Equal(mood.Date, mood.Days[^1].Date);
        Assert.Equal("hard", mood.Days[^3].Mine!.Mood);
        Assert.Null(mood.Days[^3].Partner);
        Assert.Equal("great", mood.Days[^1].Partner!.Mood);
        Assert.Null(mood.Days[^1].Mine);
    }

    [Fact]
    public async Task Withdrawing_consent_deletes_my_moods_only()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await ConsentedPair(api);
        await Set(a, "good", "секрет");
        await Set(b, "even");

        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/v1/me/mood-consent")).StatusCode);

        var mine = (await a.GetFromJsonAsync<MoodDto>("/v1/couple/mood"))!;
        Assert.False(mine.Consented);
        Assert.Null(mine.Days[^1].Mine);
        Assert.Equal("even", mine.Days[^1].Partner!.Mood);
        Assert.Null((await Today(b)).Partner);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PutAsJsonAsync("/v1/couple/mood", new { mood = "good" })).StatusCode);
    }

    [Fact]
    public async Task Moods_older_than_a_year_become_monthly_counts()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await ConsentedPair(api);
        await Set(a, "good", "старая заметка");
        await Set(b, "hard");
        (a, b) = await NextDays(api, 1);
        await Set(a, "good");

        (a, _) = await NextDays(api, 370);
        await Set(a, "great");

        using var scope = api.Services.CreateScope();
        Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<MoodRetentionJob>().RunOnceAsync());

        var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
        Assert.Equal(1, await db.Moods.CountAsync());
        Assert.False(await db.Moods.AnyAsync(m => m.Note != null));
        var months = await db.MoodMonths.ToListAsync();
        Assert.Equal(2, months.Count);
        Assert.Equal(2, months.Sum(m => m.Good));
        Assert.Equal(1, months.Sum(m => m.Hard));
        Assert.All(months, m => Assert.Equal(1, m.Month.Day));
    }
}
