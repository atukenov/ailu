using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ailu.Api.Data;
using Ailu.Api.Features.Auth;
using Ailu.Api.Features.Daily;
using Ailu.Api.Features.Memories;
using Ailu.Api.Features.Mood;
using Ailu.Api.Features.Pairing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Ailu.Api.Tests;

public class AccountDeletionTests
{
    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(40, 30);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.SkyBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();
    }

    private static async Task<Guid> MemoryWithPhoto(HttpClient client, string title)
    {
        var memory = (await (await client.PostAsJsonAsync("/v1/couple/memories", new { title })).Content
            .ReadFromJsonAsync<MemoryDto>())!;
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "photo.jpg");
        (await client.PostAsync($"/v1/couple/memories/{memory.Id}/photos", form)).EnsureSuccessStatusCode();
        return memory.Id;
    }

    [Fact]
    public async Task Deleting_an_account_removes_the_persons_data_and_keeps_the_couples_history_anonymous()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var aId = (await a.GetFromJsonAsync<UserDto>("/v1/me"))!.Id;

        // A shared day: both answer, both do the task, both mark a mood.
        var today = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        await a.PostAsJsonAsync($"/v1/couple/questions/{today.Question.Id}/answer", new { text = "Ответ Алмаза" });
        await b.PostAsJsonAsync($"/v1/couple/questions/{today.Question.Id}/answer", new { text = "Ответ Нурай" });
        await a.PostAsync($"/v1/couple/tasks/{today.Task.Id}/accept", null);
        await b.PostAsync($"/v1/couple/tasks/{today.Task.Id}/accept", null);
        await a.PostAsync($"/v1/couple/tasks/{today.Task.Id}/complete", null);
        await b.PostAsync($"/v1/couple/tasks/{today.Task.Id}/complete", null);
        (await a.PutAsync("/v1/me/mood-consent", null)).EnsureSuccessStatusCode();
        (await a.PutAsJsonAsync("/v1/couple/mood", new { mood = "good", note = "устал" })).EnsureSuccessStatusCode();

        var aMemory = await MemoryWithPhoto(a, "Его воспоминание");
        var bMemory = await MemoryWithPhoto(b, "Её воспоминание");
        (await a.PutAsync($"/v1/couple/memories/{bMemory}/reaction", null)).EnsureSuccessStatusCode();
        (await a.PutAsJsonAsync("/v1/me/devices", new { pushToken = "ExponentPushToken[a]", platform = "ios" })).EnsureSuccessStatusCode();

        int earnedBefore;
        using (var scope = api.Services.CreateScope())
            earnedBefore = (await scope.ServiceProvider.GetRequiredService<AiluDb>().Couples.SingleAsync()).NurEarned;

        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/v1/me")).StatusCode);

        // The old access token stops working at once.
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/v1/me")).StatusCode);

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
            var user = await db.Users.SingleAsync(u => u.Id == aId);
            Assert.Null(user.Phone);
            Assert.Null(user.Name);
            Assert.Null(user.MoodConsentAt);
            Assert.NotNull(user.DeletedAt);

            Assert.False(await db.RefreshTokens.AnyAsync(t => t.UserId == aId));
            Assert.False(await db.Devices.AnyAsync(d => d.UserId == aId));
            Assert.False(await db.Moods.AnyAsync(m => m.UserId == aId));
            Assert.False(await db.MemoryReactions.AnyAsync(r => r.UserId == aId));
            Assert.False(await db.PhoneCodes.AnyAsync(c => c.Phone == "+77010000001"));
            Assert.False(await db.Invitations.AnyAsync(i => i.InviterUserId == aId));

            // His memory and its photo files are gone; hers stays, with her photo still counted.
            Assert.False(await db.Memories.AnyAsync(m => m.Id == aMemory));
            Assert.True(await db.Memories.AnyAsync(m => m.Id == bMemory && m.DeletedAt == null));
            var couple = await db.Couples.SingleAsync();
            Assert.Equal(1, couple.PhotoCount);
            var photoFolders = Directory.GetDirectories(Path.Combine(api.PhotoRoot, "couples", couple.Id.ToString("N")));
            Assert.Single(photoFolders);

            // His answer text is gone, hers stays; the нұр and the couple's history stay.
            var q = await db.DailyQuestions.SingleAsync();
            Assert.Null(q.AnswerA);
            Assert.Equal("Ответ Нурай", q.AnswerB);
            Assert.Equal(earnedBefore, couple.NurEarned);
            Assert.True(await db.NurLedger.AnyAsync());
            Assert.True(await db.CoupleTasks.AnyAsync(t => t.ConfirmedAt != null));

            // The couple ended, for both.
            Assert.Equal(CoupleStatus.Ended, couple.Status);
            Assert.All(await db.CoupleMembers.ToListAsync(), m => Assert.NotNull(m.LeftAt));
        }

        // The partner can pair again, and the same phone number can sign up as a brand-new person.
        Assert.Equal("no_couple", await CodeOf(await b.GetAsync("/v1/couple")));
        api.Clock.Advance(TimeSpan.FromMinutes(2));
        var again = await api.SignInAsync("+77010000001");
        var fresh = (await again.GetFromJsonAsync<UserDto>("/v1/me"))!;
        Assert.NotEqual(aId, fresh.Id);
        Assert.Null(fresh.Name);
        (await again.PostAsJsonAsync("/v1/couple/invitations", new { phone = "+77010000002" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Goals_the_partner_never_joined_go_with_the_account_and_shared_ones_stay()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var shared = await (await a.PostAsJsonAsync("/v1/couple/goals",
            new { title = "Море", category = "travel", steps = new[] { "Билеты" } })).Content.ReadFromJsonAsync<JsonElement>();
        var sharedId = shared.GetProperty("id").GetGuid();
        (await b.PostAsync($"/v1/couple/goals/{sharedId}/join", null)).EnsureSuccessStatusCode();
        var proposal = await (await a.PostAsJsonAsync("/v1/couple/goals",
            new { title = "Ремонт", category = "home", steps = new[] { "Смета" } })).Content.ReadFromJsonAsync<JsonElement>();
        var proposalId = proposal.GetProperty("id").GetGuid();

        (await a.DeleteAsync("/v1/me")).EnsureSuccessStatusCode();

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
        Assert.Null((await db.Goals.SingleAsync(g => g.Id == sharedId)).RemovedAt);
        Assert.NotNull((await db.Goals.SingleAsync(g => g.Id == proposalId)).RemovedAt);
    }

    [Fact]
    public async Task Subscription_records_stay_after_the_payer_deletes_the_account()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var aId = (await a.GetFromJsonAsync<UserDto>("/v1/me"))!.Id;
        (await a.PostAsync("/v1/billing/trial", null)).EnsureSuccessStatusCode();

        (await a.DeleteAsync("/v1/me")).EnsureSuccessStatusCode();

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
        Assert.True(await db.Subscriptions.AnyAsync(x => x.PayerUserId == aId));
    }

    [Fact]
    public async Task Someone_without_a_couple_can_delete_their_account()
    {
        await using var api = await ApiFactory.StartAsync();
        var a = await api.SignInAsync("+77010000001", "Алмаз");
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/v1/me")).StatusCode);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}

public class ReviewLoginTests
{
    private const string ReviewPhone = "+77000000000";
    private const string ReviewCode = "246810";

    private static Task<ApiFactory> StartAsync() => ApiFactory.StartAsync(new Dictionary<string, string>
    {
        ["Auth:ReviewPhone"] = ReviewPhone,
        ["Auth:ReviewCode"] = ReviewCode
    });

    private static async Task<HttpClient> SignInAsReviewer(ApiFactory api)
    {
        var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/v1/auth/phone/request", new { phone = "8 700 000 00 00" })).StatusCode);
        var verify = await client.PostAsJsonAsync("/v1/auth/phone/verify", new { phone = ReviewPhone, code = ReviewCode });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var tokens = (await verify.Content.ReadFromJsonAsync<TokenPair>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    [Fact]
    public async Task Reviewer_signs_in_with_the_fixed_code_to_a_couple_with_history()
    {
        await using var api = await StartAsync();
        var r = await SignInAsReviewer(api);

        var me = (await r.GetFromJsonAsync<UserDto>("/v1/me"))!;
        Assert.NotNull(me.Name);
        var couple = (await r.GetFromJsonAsync<CoupleDto>("/v1/couple"))!;
        Assert.Equal("Айгерим", couple.Partner!.Name);
        Assert.True(couple.DaysTogether > 12);

        var today = (await r.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        Assert.Equal(12, today.Streak.Days);
        Assert.True(today.Phase.Earned >= 500);
        Assert.True(today.Task.AcceptedByPartner);
        Assert.True(today.Question.PartnerAnswered);
        Assert.NotEmpty((await r.GetFromJsonAsync<MemoryPage>("/v1/couple/memories"))!.Items);

        // The whole daily loop works alone: answer reveals both, the partner confirms the task.
        var answered = await (await r.PostAsJsonAsync($"/v1/couple/questions/{today.Question.Id}/answer", new { text = "Тест" }))
            .Content.ReadFromJsonAsync<QuestionDto>();
        Assert.NotNull(answered!.PartnerAnswer);
        (await r.PostAsync($"/v1/couple/tasks/{today.Task.Id}/accept", null)).EnsureSuccessStatusCode();
        (await r.PostAsync($"/v1/couple/tasks/{today.Task.Id}/complete", null)).EnsureSuccessStatusCode();
        var after = (await r.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        Assert.Equal(TaskState.Done, after.Task.State);
        Assert.Equal(13, after.Streak.Days);

        var mood = (await r.GetFromJsonAsync<MoodDto>("/v1/couple/mood"))!;
        Assert.NotNull(mood.Days.Single(d => d.Date == after.Date).Partner);

        // No SMS goes to the review number, and the account can't text anyone else.
        Assert.Throws<KeyNotFoundException>(() => api.Sms.LastCode(ReviewPhone));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await r.PostAsJsonAsync("/v1/couple/invitations", new { phone = "+77010000002" })).StatusCode);
    }

    [Fact]
    public async Task Reviewer_can_delete_the_account_and_gets_a_fresh_demo_couple_next_time()
    {
        await using var api = await StartAsync();
        var r = await SignInAsReviewer(api);
        var first = (await r.GetFromJsonAsync<CoupleDto>("/v1/couple"))!.Id;
        (await r.DeleteAsync("/v1/me")).EnsureSuccessStatusCode();

        api.Clock.Advance(TimeSpan.FromMinutes(2));
        var again = await SignInAsReviewer(api);
        var second = (await again.GetFromJsonAsync<CoupleDto>("/v1/couple"))!;
        Assert.NotEqual(first, second.Id);
        Assert.Equal("Айгерим", second.Partner!.Name);
    }

    [Fact]
    public async Task Without_the_settings_the_review_number_is_an_ordinary_number()
    {
        await using var api = await ApiFactory.StartAsync();
        var c = api.CreateClient();
        await c.PostAsJsonAsync("/v1/auth/phone/request", new { phone = ReviewPhone });
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await c.PostAsJsonAsync("/v1/auth/phone/verify", new { phone = ReviewPhone, code = ReviewCode })).StatusCode);
        Assert.Matches(@"^\d{6}$", api.Sms.LastCode(ReviewPhone));
    }
}
