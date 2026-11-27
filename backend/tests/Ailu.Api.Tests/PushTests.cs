using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Ailu.Api.Data;
using Ailu.Api.Features.Daily;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ailu.Api.Tests;

public class PushTests
{
    // Almaty is UTC+5.
    private static DateTimeOffset Almaty(int day, int hour, int minute = 0) =>
        new DateTimeOffset(2026, 10, day + 1, hour, minute, 0, TimeSpan.FromHours(5)).ToUniversalTime(); // the test clock starts on Oct 1

    private static async Task RunJob(ApiFactory api)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DailyPushJob>().RunOnceAsync();
    }

    private static async Task<(HttpClient A, HttpClient B)> PairWithDevices(ApiFactory api)
    {
        var (a, b) = await api.PairAsync();
        (await a.PutAsJsonAsync("/v1/me/devices", new { pushToken = "ExponentPushToken[a]", platform = "ios" })).EnsureSuccessStatusCode();
        (await b.PutAsJsonAsync("/v1/me/devices", new { pushToken = "ExponentPushToken[b]", platform = "android" })).EnsureSuccessStatusCode();
        return (a, b);
    }

    [Fact]
    public async Task Morning_push_brings_the_task_to_both_once()
    {
        await using var api = await ApiFactory.StartAsync();
        api.Clock.SetUtcNow(Almaty(1, 8, 0));
        var (a, _) = await PairWithDevices(api);
        var title = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Task.Title;

        api.Clock.SetUtcNow(Almaty(1, 8, 59));
        await RunJob(api);
        Assert.Empty(api.Push.Sent);

        api.Clock.SetUtcNow(Almaty(1, 9, 0));
        await RunJob(api);
        api.Clock.Advance(TimeSpan.FromMinutes(1));
        await RunJob(api);

        Assert.Equal(2, api.Push.Sent.Count);
        Assert.Equal(["ExponentPushToken[a]", "ExponentPushToken[b]"], api.Push.Sent.Select(p => p.To).Order());
        Assert.All(api.Push.Sent, p => Assert.Equal(title, p.Body));
    }

    [Fact]
    public async Task No_morning_push_after_the_window()
    {
        await using var api = await ApiFactory.StartAsync();
        api.Clock.SetUtcNow(Almaty(1, 12, 30));
        await PairWithDevices(api);
        await RunJob(api);
        Assert.Empty(api.Push.Sent);
    }

    [Fact]
    public async Task Evening_push_only_to_the_partner_who_has_not_answered()
    {
        await using var api = await ApiFactory.StartAsync();
        api.Clock.SetUtcNow(Almaty(1, 18, 0));
        var (a, b) = await PairWithDevices(api);
        var qid = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Question.Id;
        (await a.PostAsJsonAsync($"/v1/couple/questions/{qid}/answer", new { text = "Лето" })).EnsureSuccessStatusCode();

        api.Clock.SetUtcNow(Almaty(1, 20, 0));
        await RunJob(api);
        await RunJob(api);

        var push = Assert.Single(api.Push.Sent);
        Assert.Equal("ExponentPushToken[b]", push.To);
        Assert.Contains("Алмаз", push.Body);
    }

    [Fact]
    public async Task No_evening_push_when_nobody_or_both_answered()
    {
        await using var api = await ApiFactory.StartAsync();
        api.Clock.SetUtcNow(Almaty(1, 18, 0));
        var (a, b) = await PairWithDevices(api);
        var qid = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Question.Id;

        api.Clock.SetUtcNow(Almaty(1, 20, 0));
        await RunJob(api);
        Assert.Empty(api.Push.Sent);

        (a, b) = (await api.SignInAsync("+77010000001"), await api.SignInAsync("+77010000002"));
        await a.PostAsJsonAsync($"/v1/couple/questions/{qid}/answer", new { text = "Лето" });
        await b.PostAsJsonAsync($"/v1/couple/questions/{qid}/answer", new { text = "Зима" });
        await RunJob(api);
        Assert.Empty(api.Push.Sent);
    }

    [Fact]
    public async Task Token_follows_the_last_person_to_sign_in_on_the_device()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await a.PutAsJsonAsync("/v1/me/devices", new { pushToken = "ExponentPushToken[shared]", platform = "ios" });
        await b.PutAsJsonAsync("/v1/me/devices", new { pushToken = "ExponentPushToken[shared]", platform = "ios" });

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
            var device = await db.Devices.SingleAsync();
            Assert.Equal((await db.Users.SingleAsync(u => u.Phone == "+77010000002")).Id, device.UserId);
        }

        // A can no longer remove it; B can.
        await a.PostAsJsonAsync("/v1/me/devices/unregister", new { pushToken = "ExponentPushToken[shared]", platform = "ios" });
        using (var scope = api.Services.CreateScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AiluDb>().Devices.CountAsync());
        Assert.Equal(HttpStatusCode.NoContent,
            (await b.PostAsJsonAsync("/v1/me/devices/unregister", new { pushToken = "ExponentPushToken[shared]", platform = "ios" })).StatusCode);
        using (var scope = api.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AiluDb>().Devices.CountAsync());
    }

    [Fact]
    public async Task Rejects_unknown_platforms()
    {
        await using var api = await ApiFactory.StartAsync();
        var a = await api.SignInAsync("+77010000001");
        var res = await a.PutAsJsonAsync("/v1/me/devices", new { pushToken = "ExponentPushToken[x]", platform = "web" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

public class FcmPushSenderTests
{
    private sealed class FakeFcm : HttpMessageHandler
    {
        public int TokenRequests;
        public ConcurrentQueue<(string Url, string? Auth, string Body)> Sends { get; } = new();
        public HashSet<string> Gone { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (url == "https://oauth2.googleapis.com/token")
            {
                Interlocked.Increment(ref TokenRequests);
                Assert.Contains("jwt-bearer", body);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { access_token = "at-1", expires_in = 3600, token_type = "Bearer" })
                };
            }

            Sends.Enqueue((url, request.Headers.Authorization?.ToString(), body));
            using var doc = JsonDocument.Parse(body);
            var token = doc.RootElement.GetProperty("message").GetProperty("token").GetString()!;
            if (Gone.Contains(token))
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"error":{"code":404,"status":"NOT_FOUND","details":[{"errorCode":"UNREGISTERED"}]}}""")
                };
            if (token.StartsWith("ExponentPushToken"))
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""{"error":{"code":400,"message":"The registration token is not a valid FCM registration token","status":"INVALID_ARGUMENT"}}""")
                };
            if (token == "flaky") return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"name":"projects/ailu-test/messages/1"}""") };
        }
    }

    private sealed class Cleanup : IPushTokenCleanup
    {
        public List<string> Removed { get; } = [];
        public Task RemoveAsync(IReadOnlyCollection<string> tokens, CancellationToken ct = default)
        {
            Removed.AddRange(tokens);
            return Task.CompletedTask;
        }
    }

    private static string ServiceAccountJson()
    {
        using var rsa = RSA.Create(2048);
        return JsonSerializer.Serialize(new
        {
            type = "service_account",
            project_id = "ailu-test",
            client_email = "push@ailu-test.iam.gserviceaccount.com",
            private_key = rsa.ExportPkcs8PrivateKeyPem(),
            token_uri = "https://oauth2.googleapis.com/token"
        });
    }

    private static (FcmPushSender Sender, FakeFcm Fcm, Cleanup Cleanup, FakeTimeProvider Clock) Create()
    {
        var fcm = new FakeFcm();
        var cleanup = new Cleanup();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero));
        var sender = new FcmPushSender(new HttpClient(fcm), new FirebaseOptions { ServiceAccountJson = ServiceAccountJson() },
            cleanup, clock, NullLogger<FcmPushSender>.Instance);
        return (sender, fcm, cleanup, clock);
    }

    [Fact]
    public async Task Sends_each_message_to_fcm_v1_with_the_project_from_the_key()
    {
        var (sender, fcm, _, _) = Create();

        await sender.SendAsync([new PushMessage("tok-a", "Задание дня", "Обнимитесь"), new PushMessage("tok-b", "T", "B")]);

        Assert.Equal(2, fcm.Sends.Count);
        Assert.All(fcm.Sends, s =>
        {
            Assert.Equal("https://fcm.googleapis.com/v1/projects/ailu-test/messages:send", s.Url);
            Assert.Equal("Bearer at-1", s.Auth);
        });
        var a = fcm.Sends.Single(s => s.Body.Contains("tok-a"));
        using var doc = JsonDocument.Parse(a.Body);
        var m = doc.RootElement.GetProperty("message");
        Assert.Equal("Задание дня", m.GetProperty("notification").GetProperty("title").GetString());
        Assert.Equal("Обнимитесь", m.GetProperty("notification").GetProperty("body").GetString());
        Assert.Equal("default", m.GetProperty("android").GetProperty("notification").GetProperty("channel_id").GetString());
        Assert.Equal("default", m.GetProperty("apns").GetProperty("payload").GetProperty("aps").GetProperty("sound").GetString());
    }

    [Fact]
    public async Task Reuses_the_access_token_until_it_nearly_expires()
    {
        var (sender, fcm, _, clock) = Create();

        await sender.SendAsync([new PushMessage("tok-a", "T", "B")]);
        clock.Advance(TimeSpan.FromMinutes(50));
        await sender.SendAsync([new PushMessage("tok-a", "T", "B")]);
        Assert.Equal(1, fcm.TokenRequests);

        clock.Advance(TimeSpan.FromMinutes(10));
        await sender.SendAsync([new PushMessage("tok-a", "T", "B")]);
        Assert.Equal(2, fcm.TokenRequests);
    }

    [Fact]
    public async Task Removes_tokens_fcm_no_longer_knows_and_keeps_the_rest()
    {
        var (sender, fcm, cleanup, _) = Create();
        fcm.Gone.Add("tok-gone");

        await sender.SendAsync([
            new PushMessage("tok-ok", "T", "B"),
            new PushMessage("tok-gone", "T", "B"),
            new PushMessage("ExponentPushToken[old]", "T", "B"),
            new PushMessage("flaky", "T", "B")
        ]);

        Assert.Equal(4, fcm.Sends.Count);
        Assert.Equal(["ExponentPushToken[old]", "tok-gone"], cleanup.Removed.Order());
    }

    [Fact]
    public async Task Stale_devices_are_deleted_from_the_database()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        (await a.PutAsJsonAsync("/v1/me/devices", new { pushToken = "fcm-stale", platform = "android" })).EnsureSuccessStatusCode();
        (await a.PutAsJsonAsync("/v1/me/devices", new { pushToken = "fcm-live", platform = "ios" })).EnsureSuccessStatusCode();

        await new DbPushTokenCleanup(api.Services.GetRequiredService<IServiceScopeFactory>()).RemoveAsync(["fcm-stale"]);

        using var scope = api.Services.CreateScope();
        var tokens = await scope.ServiceProvider.GetRequiredService<AiluDb>().Devices.Select(d => d.PushToken).ToListAsync();
        Assert.Equal(["fcm-live"], tokens);
    }
}
