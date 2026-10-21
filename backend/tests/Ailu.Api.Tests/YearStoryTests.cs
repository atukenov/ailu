using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Features.Auth;
using Ailu.Api.Features.Memories;
using Ailu.Api.Features.YearStory;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Ailu.Api.Tests;

public class YearStoryPlanTests
{
    private static readonly StoryStats Stats = new(100, 30, 50, 4000);

    private static StoryMemory Memory(int month, int day, bool favorite = false, int reactions = 0, string? place = null) =>
        new(Guid.NewGuid(), new DateOnly(2026, month, day), $"{month}/{day}", place, Guid.NewGuid(), 1, reactions, favorite, false);

    [Fact]
    public void A_full_year_makes_exactly_a_minute_in_date_order()
    {
        var memories = Enumerable.Range(1, 12).SelectMany(m => new[] { Memory(m, 5), Memory(m, 20) }).ToList();
        var dates = new List<StoryDate> { new(StorySlideKind.DaysTogether, new DateOnly(2026, 4, 10), "days:100", Days: 100) };

        var slides = YearStoryPlan.Build(2026, memories, dates, Stats);

        Assert.Equal(YearStoryPlan.TotalMs, YearStoryPlan.DurationMs(slides));
        Assert.Equal(StorySlideKind.Intro, slides[0].Kind);
        Assert.Equal(StorySlideKind.Outro, slides[^1].Kind);
        Assert.Equal(Stats, slides[^1].Stats);
        var middle = slides[1..^1];
        Assert.Equal(middle.OrderBy(s => s.Date).Select(s => s.Date), middle.Select(s => s.Date));
        Assert.Contains(middle, s => s.Kind == StorySlideKind.DaysTogether);
        // (60 - 4 intro - 5 outro - 3 date) / 4 = 12 photos, one from each month before any second one.
        var picked = middle.Where(s => s.Kind == StorySlideKind.Memory).ToList();
        Assert.Equal(12, picked.Count);
        Assert.Equal(12, picked.Select(s => s.Date!.Value.Month).Distinct().Count());
    }

    [Fact]
    public void Favourites_and_loved_memories_win_and_one_month_cannot_fill_the_film()
    {
        var summer = Enumerable.Range(1, 20).Select(d => Memory(7, d, reactions: 2, place: "Боровое")).ToList();
        var favorite = Memory(2, 14, favorite: true);
        var plain = Memory(3, 1);
        var slides = YearStoryPlan.Build(2026, [.. summer, favorite, plain], [], Stats);

        var picked = slides.Where(s => s.Kind == StorySlideKind.Memory).ToList();
        Assert.Contains(picked, s => s.MemoryId == favorite.Id);
        // March gets its one memory in before July's second.
        Assert.Contains(picked, s => s.MemoryId == plain.Id);
        Assert.Equal(YearStoryPlan.TotalMs, YearStoryPlan.DurationMs(slides));
    }

    [Fact]
    public void Key_dates_are_capped_and_the_anniversary_comes_first()
    {
        var dates = new List<StoryDate>
        {
            new(StorySlideKind.Achievement, new DateOnly(2026, 1, 1), "a1"),
            new(StorySlideKind.Achievement, new DateOnly(2026, 2, 1), "a2"),
            new(StorySlideKind.GoalDone, new DateOnly(2026, 3, 1), "g1"),
            new(StorySlideKind.GoalDone, new DateOnly(2026, 4, 1), "g2"),
            new(StorySlideKind.Anniversary, new DateOnly(2026, 9, 1), "years:1", Years: 1)
        };
        var slides = YearStoryPlan.Build(2026, Enumerable.Range(1, 10).Select(m => Memory(m, 3)).ToList(), dates, Stats);

        var kept = slides.Where(s => s.DurationMs == YearStoryPlan.DateMs).Select(s => s.Kind).ToList();
        Assert.Equal(YearStoryPlan.MaxDates, kept.Count);
        Assert.Contains(StorySlideKind.Anniversary, kept);
        Assert.Single(kept, k => k == StorySlideKind.Achievement);
    }

    [Fact]
    public void A_few_photos_linger_longer_but_not_forever()
    {
        var slides = YearStoryPlan.Build(2026, Enumerable.Range(1, 5).Select(m => Memory(m, 1)).ToList(), [], Stats);
        Assert.All(slides.Where(s => s.Kind == StorySlideKind.Memory), s => Assert.Equal(YearStoryPlan.MaxMemoryMs, s.DurationMs));
        Assert.True(YearStoryPlan.DurationMs(slides) < YearStoryPlan.TotalMs);
    }
}

public class YearStoryTests
{
    private static readonly DateTimeOffset December = new(2026, 12, 1, 6, 0, 0, TimeSpan.Zero);

    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(64, 48);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Pink);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }

    private static async Task<MemoryDto> PhotoMemory(HttpClient client, string title, DateOnly day)
    {
        var res = await client.PostAsJsonAsync("/v1/couple/memories", new { title, happenedOn = day });
        var memory = (await res.Content.ReadFromJsonAsync<MemoryDto>())!;
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "photo.jpg");
        (await client.PostAsync($"/v1/couple/memories/{memory.Id}/photos", form)).EnsureSuccessStatusCode();
        return memory;
    }

    private static async Task<YearStoryDto> Story(HttpClient client, int? year = null) =>
        (await client.GetFromJsonAsync<YearStoryDto>($"/v1/couple/year-story{(year is null ? "" : $"?year={year}")}"))!;

    private static StoreProductPurchase Paid(string id, StoreProvider provider = StoreProvider.Apple, string? account = null) =>
        new(provider, id, provider == StoreProvider.Apple ? "kz.ailu.story.year" : "year_story", true, false, account);

    private static async Task<int> Render(ApiFactory api)
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<YearStoryJob>().RunOnceAsync();
    }

    // A couple that began on 1 October 2026 with six photo memories, two of them in November.
    private static async Task<(ApiFactory Api, HttpClient A, HttpClient B, List<MemoryDto> Memories)> CoupleWithAYear()
    {
        var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var memories = new List<MemoryDto>();
        for (var i = 0; i < 4; i++) memories.Add(await PhotoMemory(a, $"Октябрь {i + 1}", new DateOnly(2026, 10, 1)));
        api.Clock.SetUtcNow(December);
        (a, b) = (await api.SignInAsync("+77010000001"), await api.SignInAsync("+77010000002"));
        memories.Add(await PhotoMemory(b, "Первый снег", new DateOnly(2026, 11, 20)));
        memories.Add(await PhotoMemory(b, "Каток", new DateOnly(2026, 11, 28)));
        return (api, a, b, memories);
    }

    [Fact]
    public async Task The_preview_grows_during_the_year_and_opens_for_ordering_on_1_December()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        await PhotoMemory(a, "Первое свидание", new DateOnly(2026, 10, 1));

        var october = await Story(a);
        Assert.Equal(2026, october.Year);
        Assert.Equal([2026], october.Years);
        Assert.False(october.Open);
        Assert.Equal(new DateOnly(2026, 12, 1), october.OpensOn);
        Assert.Equal(1990, october.PriceKzt);
        Assert.Equal(1, october.Memories);
        Assert.Equal(YearStoryPlan.MinMemories, october.MinMemories);
        // The start of the couple is one of the year's dates; the memory slide carries a photo to show.
        Assert.Contains(october.Slides, s => s.Kind == StorySlideKind.Start);
        Assert.Contains(october.Slides, s => s is { Kind: StorySlideKind.Memory, Title: "Первое свидание" } && s.Photo != null);
        Assert.Null(october.Order);

        api.Apple.Products["t1"] = Paid("t1");
        var early = await a.PostAsJsonAsync("/v1/couple/year-story/2026/apple", new { transactionId = "t1" });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Contains("story_not_open", await early.Content.ReadAsStringAsync());

        api.Clock.SetUtcNow(December);
        a = await api.SignInAsync("+77010000001");
        Assert.True((await Story(a)).Open);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.GetAsync("/v1/couple/year-story?year=2025")).StatusCode);
    }

    [Fact]
    public async Task One_partner_buys_the_film_and_both_get_it_when_it_is_rendered()
    {
        var (api, a, b, memories) = await CoupleWithAYear();
        await using var _ = api;
        var me = (await a.GetFromJsonAsync<UserDto>("/v1/me"))!;
        (await a.PutAsJsonAsync("/v1/me/devices", new { pushToken = "ExponentPushToken[a]", platform = "ios" })).EnsureSuccessStatusCode();
        (await b.PutAsJsonAsync("/v1/me/devices", new { pushToken = "ExponentPushToken[b]", platform = "android" })).EnsureSuccessStatusCode();

        api.Apple.Products["t1"] = Paid("t1", account: me.Id.ToString());
        var res = await a.PostAsJsonAsync("/v1/couple/year-story/2026/apple", new { transactionId = "t1" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var ordered = (await res.Content.ReadFromJsonAsync<YearStoryDto>())!;
        Assert.Equal("making", ordered.Order!.Status);
        Assert.True(ordered.Order.PaidByMe);
        Assert.Equal(6, ordered.Memories);

        var partner = await Story(b);
        Assert.False(partner.Order!.PaidByMe);
        Assert.Null(partner.Order.Video);

        // The film is the storyboard paid for: a memory added later doesn't join it.
        await PhotoMemory(b, "Ёлка", new DateOnly(2026, 12, 1));
        Assert.DoesNotContain((await Story(b)).Slides, s => s.Title == "Ёлка");

        // A memory deleted since leaves the film.
        (await a.DeleteAsync($"/v1/couple/memories/{memories[0].Id}")).EnsureSuccessStatusCode();

        Assert.Equal(1, await Render(api));
        var request = api.Renderer.Requests.Single();
        Assert.Equal(5, request.Slides.Count(s => s.Kind == StorySlideKind.Memory));
        Assert.DoesNotContain(request.Slides, s => s.MemoryId == memories[0].Id);
        Assert.Equal(("Алмаз", "Нурай", "ru"), (request.NameA, request.NameB, request.Locale));
        Assert.All(request.Slides.Where(s => s.PhotoId is not null), s => Assert.True(request.PhotoKeys.ContainsKey(s.PhotoId!.Value)));
        Assert.Equal(["ExponentPushToken[a]", "ExponentPushToken[b]"],
            api.Push.Sent.Where(p => p.Title == "Видео-история 2026 года готова").Select(p => p.To).Order());

        var ready = await Story(b);
        Assert.Equal("ready", ready.Order!.Status);
        var video = await api.CreateClient().GetAsync(ready.Order.Video);
        Assert.Equal(HttpStatusCode.OK, video.StatusCode);
        Assert.Equal("video/mp4", video.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("mp4:intro,", await video.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync(ready.Order.Video!.Replace("sig=", "sig=x"))).StatusCode);

        // Nothing left to render.
        Assert.Equal(0, await Render(api));
    }

    [Fact]
    public async Task A_year_is_bought_once()
    {
        var (api, a, b, _) = await CoupleWithAYear();
        await using var _ = api;
        api.Apple.Products["t1"] = Paid("t1");
        api.Apple.Products["t2"] = Paid("t2");
        api.Apple.Products["refunded"] = Paid("refunded") with { Paid = false };

        Assert.Equal(HttpStatusCode.NotFound,
            (await a.PostAsJsonAsync("/v1/couple/year-story/2026/apple", new { transactionId = "refunded" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await a.PostAsJsonAsync("/v1/couple/year-story/2026/apple", new { transactionId = "made-up" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await a.PostAsJsonAsync("/v1/couple/year-story/2026/apple", new { transactionId = "t1" })).StatusCode);
        // The app retrying the same purchase is fine; a second purchase for the same year is refused.
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsJsonAsync("/v1/couple/year-story/2026/apple", new { transactionId = "t1" })).StatusCode);
        var twice = await b.PostAsJsonAsync("/v1/couple/year-story/2026/apple", new { transactionId = "t2" });
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Contains("story_ordered", await twice.Content.ReadAsStringAsync());

        // A purchase can pay for one film only, even in another couple.
        var (c, _) = await api.PairAsync("+77010000003", "+77010000004");
        var reused = await c.PostAsJsonAsync("/v1/couple/year-story/2026/apple", new { transactionId = "t1" });
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);

        using var scope = api.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AiluDb>().YearStories.CountAsync());
    }

    [Fact]
    public async Task A_purchase_from_another_account_is_refused()
    {
        var (api, a, _, _) = await CoupleWithAYear();
        await using var _ = api;
        api.Google.Products["tok"] = Paid("tok", StoreProvider.Google, account: Guid.NewGuid().ToString());
        var res = await a.PostAsJsonAsync("/v1/couple/year-story/2026/google", new { purchaseToken = "tok" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("purchase_other_account", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Google_purchases_are_acknowledged()
    {
        var (api, a, _, _) = await CoupleWithAYear();
        await using var _ = api;
        api.Google.Products["tok"] = Paid("tok", StoreProvider.Google) with { NeedsAcknowledge = true };
        Assert.Equal(HttpStatusCode.OK,
            (await a.PostAsJsonAsync("/v1/couple/year-story/2026/google", new { purchaseToken = "tok" })).StatusCode);
        Assert.Contains("tok", api.Google.Acknowledged);
    }

    [Fact]
    public async Task Orders_wait_for_a_renderer_and_failing_renders_are_retried_then_given_up()
    {
        var (api, a, _, _) = await CoupleWithAYear();
        await using var _ = api;
        api.Apple.Products["t1"] = Paid("t1");
        (await a.PostAsJsonAsync("/v1/couple/year-story/2026/apple", new { transactionId = "t1" })).EnsureSuccessStatusCode();

        // No renderer: the order keeps waiting and no attempt is used up.
        api.Renderer.Unavailable = true;
        Assert.Equal(0, await Render(api));
        Assert.Equal(0, await Render(api)); // not due again yet
        api.Clock.Advance(YearStoryJob.WaitForRenderer);
        api.Renderer.Unavailable = false;

        api.Renderer.Broken = true;
        for (var i = 0; i < YearStoryJob.MaxAttempts; i++)
        {
            Assert.Equal(0, await Render(api));
            api.Clock.Advance(TimeSpan.FromDays(1));
        }
        a = await api.SignInAsync("+77010000001");
        Assert.Equal("failed", (await Story(a)).Order!.Status);
        using var scope = api.Services.CreateScope();
        var story = await scope.ServiceProvider.GetRequiredService<AiluDb>().YearStories.SingleAsync();
        Assert.Equal(YearStoryJob.MaxAttempts, story.Attempts);
        Assert.Equal("render failed", story.Error);
    }
}
