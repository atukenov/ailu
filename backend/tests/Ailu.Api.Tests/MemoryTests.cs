using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Features.Daily;
using Ailu.Api.Features.Memories;
using Ailu.Api.Features.Pairing;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Ailu.Api.Tests;

public class MemoryTests
{
    private static byte[] Jpeg(int width, int height, ushort? exifOrientation = null)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SkyBlue);
            // A red block in the top-left corner, to see where rotation puts it.
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, width / 4f, height / 4f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();
        return exifOrientation is ushort o ? WithExif(jpeg, o) : jpeg;
    }

    // Inserts an APP1 EXIF segment with an Orientation tag, as a phone camera writes it.
    private static byte[] WithExif(byte[] jpeg, ushort orientation)
    {
        byte[] tiff =
        [
            (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8, // big-endian header, first IFD at 8
            0, 1, // one entry
            0x01, 0x12, 0, 3, 0, 0, 0, 1, (byte)(orientation >> 8), (byte)orientation, 0, 0, // Orientation, SHORT, 1
            0, 0, 0, 0 // no next IFD
        ];
        byte[] header = [(byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0];
        var length = 2 + header.Length + tiff.Length;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. header, .. tiff];
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, Guid memoryId, byte[] bytes, string type = "image/jpeg")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", "photo.jpg");
        return await client.PostAsync($"/v1/couple/memories/{memoryId}/photos", form);
    }

    private static async Task<MemoryDto> Create(HttpClient client, string title = "Рим, первый вечер", DateOnly? happenedOn = null)
    {
        var res = await client.PostAsJsonAsync("/v1/couple/memories", new { title, text = "Дошли пешком от вокзала", happenedOn });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<MemoryDto>())!;
    }

    private static async Task<StorageDto> Storage(HttpClient client) =>
        (await client.GetFromJsonAsync<StorageDto>("/v1/couple/storage"))!;

    private static SKBitmap Decode(byte[] bytes) => SKBitmap.Decode(bytes);

    [Fact]
    public async Task Photo_is_shrunk_stripped_counted_and_shared_with_the_partner()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var memory = await Create(a);

        var res = await Upload(a, memory.Id, Jpeg(4000, 3000, exifOrientation: 1));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var photo = Assert.Single((await res.Content.ReadFromJsonAsync<MemoryDto>())!.Photos);
        Assert.Equal((2560, 1920), (photo.Width, photo.Height));

        // The partner sees it, and the signed URLs work without a token.
        var seen = (await b.GetFromJsonAsync<MemoryPage>("/v1/couple/memories"))!.Items.Single();
        Assert.Equal("Алмаз", seen.Author.Name);
        Assert.False(seen.Mine);
        var anonymous = api.CreateClient();
        var full = await anonymous.GetByteArrayAsync(seen.Photos[0].Full);
        Assert.False(ContainsExif(full));
        using (var bitmap = Decode(full)) Assert.Equal((2560, 1920), (bitmap.Width, bitmap.Height));

        // Smaller sizes are made on first request and kept.
        using (var thumb = Decode(await anonymous.GetByteArrayAsync(seen.Photos[0].Thumb))) Assert.Equal(320, thumb.Width);
        Assert.Equal(2, Directory.GetFiles(api.PhotoRoot, "*.jpg", SearchOption.AllDirectories).Length);

        var storage = await Storage(b);
        Assert.Equal(full.Length, storage.UsedBytes);
        Assert.Equal((1, 200), (storage.Photos, storage.PhotoLimit));
        Assert.Equal("free", storage.Plan);
    }

    private static bool ContainsExif(byte[] jpeg) =>
        jpeg.AsSpan().IndexOf("Exif\0\0"u8) >= 0;

    [Fact]
    public async Task Sideways_phone_photo_is_turned_upright()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var memory = await Create(a);

        // Stored landscape, EXIF says "rotate 90° clockwise to view".
        var res = await Upload(a, memory.Id, Jpeg(400, 300, exifOrientation: 6));
        var photo = (await res.Content.ReadFromJsonAsync<MemoryDto>())!.Photos.Single();
        Assert.Equal((300, 400), (photo.Width, photo.Height));

        using var bitmap = Decode(await api.CreateClient().GetByteArrayAsync(photo.Full));
        // The red top-left corner of the stored image ends up top-right once turned clockwise.
        Assert.True(bitmap.GetPixel(290, 10).Red > 200 && bitmap.GetPixel(290, 10).Blue < 80);
        Assert.True(bitmap.GetPixel(10, 10).Blue > 150);
    }

    [Fact]
    public async Task Not_a_photo_is_refused_and_costs_nothing()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var memory = await Create(a);

        var res = await Upload(a, memory.Id, "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), "image/svg+xml");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(0, (await Storage(a)).UsedBytes);
        Assert.False(Directory.Exists(api.PhotoRoot) && Directory.GetFiles(api.PhotoRoot, "*", SearchOption.AllDirectories).Length > 0);
    }

    [Theory]
    [InlineData(CouplePlan.Free, Plans.FreePhotos, 0L)] // the 201st photo on the free plan
    [InlineData(CouplePlan.Plus, Plans.PlusPhotos, 0L)] // the 501st on Plus
    [InlineData(CouplePlan.Premium, Plans.PremiumPhotos, 0L)] // the 1 201st on Premium
    [InlineData(CouplePlan.Free, 0, 1_000_000_000L - 1000)] // or one that doesn't fit in the bytes left
    public async Task Full_quota_refuses_the_upload_and_keeps_no_file(CouplePlan plan, int photos, long bytes)
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var memory = await Create(a);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
            await db.Couples.ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Plan, plan).SetProperty(c => c.PhotoCount, photos).SetProperty(c => c.StorageBytes, bytes));
        }

        var res = await Upload(a, memory.Id, Jpeg(800, 600));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("storage_full", await res.Content.ReadAsStringAsync());
        Assert.Empty((await a.GetFromJsonAsync<MemoryDto>($"/v1/couple/memories/{memory.Id}"))!.Photos);
        Assert.Empty(Directory.GetFiles(api.PhotoRoot, "*", SearchOption.AllDirectories));
        var storage = await Storage(a);
        Assert.Equal((photos, bytes), (storage.Photos, storage.UsedBytes));
        Assert.Equal(Plans.PhotoLimit(plan), storage.PhotoLimit);
    }

    [Fact]
    public async Task A_paid_couple_keeps_adding_photos_past_the_free_limit()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var memory = await Create(a);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
            await db.Couples.ExecuteUpdateAsync(s => s.SetProperty(c => c.Plan, CouplePlan.Plus).SetProperty(c => c.PhotoCount, Plans.FreePhotos));
        }

        Assert.Equal(HttpStatusCode.OK, (await Upload(a, memory.Id, Jpeg(800, 600))).StatusCode);
        var storage = await Storage(a);
        Assert.Equal((Plans.FreePhotos + 1, Plans.PlusPhotos), (storage.Photos, storage.PhotoLimit));
    }

    [Fact]
    public async Task Only_the_author_changes_or_deletes_and_deleting_frees_the_quota()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var memory = await Create(a);
        var photo = (await (await Upload(a, memory.Id, Jpeg(800, 600))).Content.ReadFromJsonAsync<MemoryDto>())!.Photos.Single();

        Assert.Equal(HttpStatusCode.Forbidden, (await b.PatchAsJsonAsync($"/v1/couple/memories/{memory.Id}", new { title = "Моё" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.DeleteAsync($"/v1/couple/memories/{memory.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(b, memory.Id, Jpeg(80, 60))).StatusCode);

        var edited = await a.PatchAsJsonAsync($"/v1/couple/memories/{memory.Id}", new { place = "Италия" });
        var dto = (await edited.Content.ReadFromJsonAsync<MemoryDto>())!;
        Assert.Equal(("Рим, первый вечер", "Италия"), (dto.Title, dto.Place));

        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/v1/couple/memories/{memory.Id}")).StatusCode);
        Assert.Empty((await b.GetFromJsonAsync<MemoryPage>("/v1/couple/memories"))!.Items);
        var freed = await Storage(a);
        Assert.Equal((0, 0L), (freed.Photos, freed.UsedBytes));
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync(photo.Full)).StatusCode);

        // The file stays in the bin for 30 days, then the purge removes it and the rows.
        Assert.Single(Directory.GetFiles(api.PhotoRoot, "*.jpg", SearchOption.AllDirectories));
        api.Clock.Advance(TimeSpan.FromDays(29));
        Assert.Equal(0, await Purge(api));
        api.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, await Purge(api));
        Assert.Empty(Directory.GetFiles(api.PhotoRoot, "*.jpg", SearchOption.AllDirectories));
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiluDb>();
        Assert.Equal(0, await db.Memories.CountAsync());
        Assert.Equal(0, await db.MemoryPhotos.CountAsync());
    }

    private static async Task<int> Purge(ApiFactory api)
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MemoryPurgeJob>().RunOnceAsync();
    }

    [Fact]
    public async Task Partner_heart_earns_20_once_per_memory_and_three_times_a_day()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var memories = new List<MemoryDto>();
        for (var i = 0; i < 4; i++) memories.Add(await Create(a, $"Вечер {i}"));

        // The author's own heart is worth nothing.
        await a.PutAsync($"/v1/couple/memories/{memories[0].Id}/reaction", null);
        Assert.Equal(0, (await a.GetFromJsonAsync<PhaseDto>("/v1/couple/phase"))!.Earned);

        // A partner's heart, taken back and given again, still counts once.
        var hearted = await (await b.PutAsync($"/v1/couple/memories/{memories[0].Id}/reaction", null)).Content.ReadFromJsonAsync<MemoryDto>();
        Assert.True(hearted!.ReactedByMe && hearted.ReactedByPartner);
        await b.DeleteAsync($"/v1/couple/memories/{memories[0].Id}/reaction");
        await b.PutAsync($"/v1/couple/memories/{memories[0].Id}/reaction", null);
        Assert.Equal(20, (await a.GetFromJsonAsync<PhaseDto>("/v1/couple/phase"))!.Earned);

        foreach (var m in memories.Skip(1)) await b.PutAsync($"/v1/couple/memories/{m.Id}/reaction", null);
        Assert.Equal(60, (await a.GetFromJsonAsync<PhaseDto>("/v1/couple/phase"))!.Earned);

        // Hearts count toward the streak like any shared action: A added, B reacted.
        Assert.Equal(1, (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Streak.Days);
    }

    [Fact]
    public async Task Photo_links_expire_and_cannot_be_forged()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var memory = await Create(a);
        var photo = (await (await Upload(a, memory.Id, Jpeg(800, 600))).Content.ReadFromJsonAsync<MemoryDto>())!.Photos.Single();
        var anonymous = api.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(photo.Screen)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(photo.Screen.Replace("/1080?", "/2560?"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(photo.Screen[..^4] + "AAAA")).StatusCode);

        api.Clock.Advance(TimeSpan.FromHours(2.1));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(photo.Screen)).StatusCode);
        // A fresh read hands out fresh links (signing in again: the access token has expired too).
        a = await api.SignInAsync("+77010000001");
        var again = (await a.GetFromJsonAsync<MemoryDto>($"/v1/couple/memories/{memory.Id}"))!.Photos.Single();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(again.Screen)).StatusCode);
    }

    [Fact]
    public async Task Pages_go_by_the_day_it_happened_without_gaps_or_repeats()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        // Same clock for all: many memories share CreatedAt, so the cursor has to break ties.
        var today = DateOnly.FromDateTime(api.Clock.GetUtcNow().AddHours(5).DateTime);
        for (var i = 0; i < 25; i++) await Create(a, $"#{i}", today.AddDays(-(i % 5)));

        var first = (await a.GetFromJsonAsync<MemoryPage>("/v1/couple/memories"))!;
        Assert.Equal(20, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        var second = (await a.GetFromJsonAsync<MemoryPage>($"/v1/couple/memories?cursor={first.NextCursor}"))!;
        Assert.Equal(5, second.Items.Count);
        Assert.Null(second.NextCursor);

        var all = first.Items.Concat(second.Items).ToList();
        Assert.Equal(25, all.Select(m => m.Id).Distinct().Count());
        Assert.Equal(all.Select(m => m.HappenedOn).OrderDescending(), all.Select(m => m.HappenedOn));
    }

    [Fact]
    public async Task Another_couple_sees_nothing_and_cannot_attach_their_task()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var (c, _) = await api.PairAsync("+77010000003", "+77010000004");
        var memory = await Create(a);
        var theirTask = (await c.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Task.Id;

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/v1/couple/memories/{memory.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsync($"/v1/couple/memories/{memory.Id}/reaction", null)).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<MemoryPage>("/v1/couple/memories"))!.Items);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await a.PostAsJsonAsync("/v1/couple/memories", new { title = "Чужое", coupleTaskId = theirTask })).StatusCode);

        var ownTask = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.Task.Id;
        Assert.Equal(HttpStatusCode.Created,
            (await a.PostAsJsonAsync("/v1/couple/memories", new { title = "Наше", coupleTaskId = ownTask })).StatusCode);
    }

    [Fact]
    public async Task Rejects_future_dates_and_empty_titles()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/v1/couple/memories", new { title = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await a.PostAsJsonAsync("/v1/couple/memories", new { title = "Завтра", happenedOn = new DateOnly(2026, 10, 3) })).StatusCode);
    }
}
