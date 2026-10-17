using System.Net;
using System.Net.Http.Json;
using Ailu.Api.Data;
using Ailu.Api.Features.Daily;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

public class ExpoPushSenderTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(Ailu.Api.Infrastructure.ExpoPushSender.Endpoint, request.RequestUri!.ToString());
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Sends_expo_messages_in_chunks_of_100()
    {
        var capture = new Capture();
        var sender = new Ailu.Api.Infrastructure.ExpoPushSender(new HttpClient(capture),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Ailu.Api.Infrastructure.ExpoPushSender>.Instance);
        var messages = Enumerable.Range(0, 101).Select(i => new Ailu.Api.Infrastructure.PushMessage($"ExponentPushToken[{i}]", "T", "B")).ToList();

        await sender.SendAsync(messages);

        Assert.Equal(2, capture.Bodies.Count);
        using var first = System.Text.Json.JsonDocument.Parse(capture.Bodies[0]);
        Assert.Equal(100, first.RootElement.GetArrayLength());
        var m = first.RootElement[0];
        Assert.Equal("ExponentPushToken[0]", m.GetProperty("to").GetString());
        Assert.Equal("T", m.GetProperty("title").GetString());
        Assert.Equal("B", m.GetProperty("body").GetString());
        Assert.Equal("default", m.GetProperty("sound").GetString());
    }
}
