using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Ailu.Api.Data;
using Ailu.Api.Features.Auth;
using Ailu.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Ailu.Api.Tests;

public sealed class FakePush : IPushSender
{
    public ConcurrentQueue<PushMessage> Sent { get; } = new();

    public Task SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken ct = default)
    {
        foreach (var m in messages) Sent.Enqueue(m);
        return Task.CompletedTask;
    }
}

// A store that knows whatever the test tells it. Any id the test registers (a transaction id, a token)
// resolves to the purchase it maps to.
public sealed class TestStore(StoreProvider provider) : IStoreClient
{
    public ConcurrentDictionary<string, StorePurchase> Purchases { get; } = new();
    public ConcurrentQueue<string> Acknowledged { get; } = new();
    public bool Down { get; set; }

    public StoreProvider Provider => provider;

    public Task<StorePurchase?> FetchAsync(string storeId, CancellationToken ct = default)
    {
        if (Down) throw new StoreUnavailableException("down");
        return Task.FromResult(Purchases.GetValueOrDefault(storeId));
    }

    public Task AcknowledgeAsync(StorePurchase purchase, CancellationToken ct = default)
    {
        Acknowledged.Enqueue(purchase.StoreId);
        return Task.CompletedTask;
    }

    // One-time purchases, keyed by store id; the product id must match, as Google's lookup requires.
    public ConcurrentDictionary<string, StoreProductPurchase> Products { get; } = new();

    public Task<StoreProductPurchase?> FetchProductAsync(string productId, string storeId, CancellationToken ct = default)
    {
        if (Down) throw new StoreUnavailableException("down");
        var found = Products.GetValueOrDefault(storeId);
        return Task.FromResult(found?.ProductId == productId ? found : null);
    }

    public Task AcknowledgeProductAsync(StoreProductPurchase purchase, CancellationToken ct = default)
    {
        Acknowledged.Enqueue(purchase.StoreId);
        return Task.CompletedTask;
    }
}

// Renders a film as a few bytes naming its slides, or fails or waits when the test says so.
public sealed class TestRenderer : IStoryRenderer
{
    public ConcurrentQueue<StoryRenderRequest> Requests { get; } = new();
    public bool Unavailable { get; set; }
    public bool Broken { get; set; }

    public Task<byte[]> RenderAsync(StoryRenderRequest request, CancellationToken ct = default)
    {
        if (Unavailable) throw new RendererUnavailableException("none");
        if (Broken) throw new InvalidOperationException("render failed");
        Requests.Enqueue(request);
        return Task.FromResult(System.Text.Encoding.UTF8.GetBytes($"mp4:{string.Join(",", request.Slides.Select(s => s.Kind))}"));
    }
}

public sealed class FakeSms : ISmsSender
{
    private readonly ConcurrentDictionary<string, string> last = new();

    public Task SendAsync(string phone, string text, CancellationToken ct = default)
    {
        last[phone] = text;
        return Task.CompletedTask;
    }

    public string LastCode(string phone) => Regex.Match(last[phone], @"\d{6}").Value;
}

// Runs the real API against a throwaway PostgreSQL database. Point AILU_TEST_PG at a server;
// the default matches a local install and the CI service container.
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero)); // 11:00 in Almaty
    public FakeSms Sms { get; } = new();
    public FakePush Push { get; } = new();
    public TestStore Apple { get; } = new(StoreProvider.Apple);
    public TestStore Google { get; } = new(StoreProvider.Google);
    public TestRenderer Renderer { get; } = new();
    public const string GoogleWebhookSecret = "test-webhook-secret";

    public string PhotoRoot { get; } = Path.Combine(Path.GetTempPath(), $"ailu_photos_{Guid.NewGuid():N}");

    private readonly string connectionString;
    private readonly IReadOnlyDictionary<string, string> settings;

    public ApiFactory(IReadOnlyDictionary<string, string>? settings = null)
    {
        this.settings = settings ?? new Dictionary<string, string>();
        var server = Environment.GetEnvironmentVariable("AILU_TEST_PG")
                     ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";
        connectionString = $"{server};Database=ailu_test_{Guid.NewGuid():N}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Ailu", connectionString);
        builder.UseSetting("Auth:SigningKey", "test-signing-key-0123456789abcdef0123456789");
        // Tests run the push job directly at the times they choose.
        builder.UseSetting("Push:SchedulerEnabled", "false");
        builder.UseSetting("Photos:PurgeEnabled", "false");
        builder.UseSetting("Mood:RetentionEnabled", "false");
        builder.UseSetting("Photos:Root", PhotoRoot);
        builder.UseSetting("Billing:ExpiryEnabled", "false");
        builder.UseSetting("YearStory:RenderEnabled", "false");
        builder.UseSetting("Billing:Google:WebhookSecret", GoogleWebhookSecret);
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
            s.RemoveAll<ISmsSender>();
            s.AddSingleton<ISmsSender>(Sms);
            s.RemoveAll<IPushSender>();
            s.AddSingleton<IPushSender>(Push);
            s.RemoveAll<IStoreClient>();
            s.AddSingleton<IStoreClient>(Apple);
            s.AddSingleton<IStoreClient>(Google);
            s.RemoveAll<IStoryRenderer>();
            s.AddSingleton<IStoryRenderer>(Renderer);
        });
    }

    // One database per test: tests move the clock and create couples, so they must not share state.
    public static async Task<ApiFactory> StartAsync(IReadOnlyDictionary<string, string>? settings = null)
    {
        var api = new ApiFactory(settings);
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AiluDb>().Database.MigrateAsync();
        return api;
    }

    public override async ValueTask DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AiluDb>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
        if (Directory.Exists(PhotoRoot)) Directory.Delete(PhotoRoot, recursive: true);
    }

    public async Task<HttpClient> SignInAsync(string phone, string? name = null)
    {
        var client = CreateClient();
        (await client.PostAsJsonAsync("/v1/auth/phone/request", new { phone })).EnsureSuccessStatusCode();
        var verify = await client.PostAsJsonAsync("/v1/auth/phone/verify", new { phone, code = Sms.LastCode(PhoneNumber.Normalize(phone)!) });
        verify.EnsureSuccessStatusCode();
        var tokens = (await verify.Content.ReadFromJsonAsync<TokenPair>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        if (name is not null) (await client.PatchAsJsonAsync("/v1/me", new { name })).EnsureSuccessStatusCode();
        return client;
    }

    public async Task<(HttpClient A, HttpClient B)> PairAsync(string phoneA = "+77010000001", string phoneB = "+77010000002")
    {
        var a = await SignInAsync(phoneA, "Алмаз");
        var b = await SignInAsync(phoneB, "Нурай");
        (await a.PostAsJsonAsync("/v1/couple/invitations", new { phone = phoneB })).EnsureSuccessStatusCode();
        (await b.PostAsync($"/v1/couple/invitations/{Sms.LastCode(phoneB)}/accept", null)).EnsureSuccessStatusCode();
        return (a, b);
    }
}
