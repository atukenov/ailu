using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ailu.Api.Data;
using Ailu.Api.Features.Memories;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Ailu.Api.Tests;

public class StorageTests
{
    internal static byte[] Jpeg(int width = 800, int height = 600)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.SkyBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();
    }

    internal static async Task<MemoryDto> MemoryWithPhoto(HttpClient client, string title = "Рим")
    {
        var created = await client.PostAsJsonAsync("/v1/couple/memories", new { title });
        var memory = (await created.Content.ReadFromJsonAsync<MemoryDto>())!;
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "photo.jpg");
        var uploaded = await client.PostAsync($"/v1/couple/memories/{memory.Id}/photos", form);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        return (await uploaded.Content.ReadFromJsonAsync<MemoryDto>())!;
    }

    private static string[] Files(string root) =>
        Directory.Exists(root)
            ? Directory.GetFiles(root, "*.jpg", SearchOption.AllDirectories).Select(Path.GetFileName).Order().ToArray()!
            : [];

    private static async Task<int> Run<TJob>(ApiFactory api, Func<TJob, Task<int>> run) where TJob : notnull
    {
        using var scope = api.Services.CreateScope();
        return await run(scope.ServiceProvider.GetRequiredService<TJob>());
    }

    [Fact]
    public async Task A_year_after_upload_full_and_screen_sizes_move_to_the_archive_and_still_open()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var photo = (await MemoryWithPhoto(a)).Photos.Single();
        (await api.CreateClient().GetAsync(photo.Screen)).EnsureSuccessStatusCode();
        Assert.Equal(["1080.jpg", "2560.jpg"], Files(api.PhotoRoot));

        api.Clock.Advance(TimeSpan.FromDays(364));
        Assert.Equal(0, await Run<PhotoArchiveJob>(api, j => j.RunOnceAsync()));
        api.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, await Run<PhotoArchiveJob>(api, j => j.RunOnceAsync()));
        Assert.Equal(0, await Run<PhotoArchiveJob>(api, j => j.RunOnceAsync()));
        Assert.Empty(Files(api.PhotoRoot));
        Assert.Equal(["1080.jpg", "2560.jpg"], Files(api.ArchiveRoot));

        // The partner opens it as before; the thumbnail, made now, is kept in the standard class.
        var b = await api.SignInAsync("+77010000002");
        var fresh = (await b.GetFromJsonAsync<MemoryPage>("/v1/couple/memories"))!.Items.Single().Photos.Single();
        var anon = api.CreateClient();
        foreach (var url in new[] { fresh.Full, fresh.Screen, fresh.Thumb })
            Assert.Equal("image/jpeg", (await anon.GetAsync(url)).EnsureSuccessStatusCode().Content.Headers.ContentType?.MediaType);
        Assert.Equal(["320.jpg"], Files(api.PhotoRoot));
    }

    [Fact]
    public async Task A_screen_size_first_asked_for_after_archiving_is_made_in_the_archive()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        await MemoryWithPhoto(a);
        api.Clock.Advance(TimeSpan.FromDays(366));
        Assert.Equal(1, await Run<PhotoArchiveJob>(api, j => j.RunOnceAsync()));

        a = await api.SignInAsync("+77010000001");
        var photo = (await a.GetFromJsonAsync<MemoryPage>("/v1/couple/memories"))!.Items.Single().Photos.Single();
        (await api.CreateClient().GetAsync(photo.Screen)).EnsureSuccessStatusCode();
        Assert.Equal(["1080.jpg", "2560.jpg"], Files(api.ArchiveRoot));
        Assert.Empty(Files(api.PhotoRoot));
    }

    [Fact]
    public async Task Deleting_an_archived_memory_purges_its_files_from_every_class_after_the_bin()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var archived = await MemoryWithPhoto(a, "Старое");
        var binned = await MemoryWithPhoto(a, "В корзине");
        (await a.DeleteAsync($"/v1/couple/memories/{binned.Id}")).EnsureSuccessStatusCode();
        api.Clock.Advance(TimeSpan.FromDays(366));

        // The purge got the binned one; it never went to the archive.
        Assert.Equal(1, await Run<MemoryPurgeJob>(api, j => j.RunOnceAsync()));
        Assert.Equal(1, await Run<PhotoArchiveJob>(api, j => j.RunOnceAsync()));
        Assert.Equal(["2560.jpg"], Files(api.ArchiveRoot));

        a = await api.SignInAsync("+77010000001");
        (await a.DeleteAsync($"/v1/couple/memories/{archived.Id}")).EnsureSuccessStatusCode();
        api.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(1, await Run<MemoryPurgeJob>(api, j => j.RunOnceAsync()));
        Assert.Empty(Files(api.PhotoRoot));
        Assert.Empty(Files(api.ArchiveRoot));
        using var scope = api.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AiluDb>().MemoryPhotos.CountAsync());
    }

    private static Guid KeptCouple(ApiFactory api)
    {
        using var scope = api.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AiluDb>().Couples.Single().Id;
    }

    [Fact]
    public async Task Files_no_photo_points_to_are_swept_after_a_day()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        var kept = (await MemoryWithPhoto(a)).Photos.Single();
        var store = api.Services.GetRequiredService<IPhotoStore>();
        var coupleId = Guid.NewGuid();
        var orphan = Guid.NewGuid();
        await store.PutAsync(PhotoKeys.File(coupleId, orphan, 2560), Jpeg(), StorageTier.Standard);
        await store.PutAsync(PhotoKeys.File(coupleId, orphan, 1080), Jpeg(), StorageTier.Archive);
        // File times come from the disk, not the test clock; line them up with the test clock.
        foreach (var root in new[] { api.PhotoRoot, api.ArchiveRoot })
        foreach (var f in Directory.GetFiles(root, "*.jpg", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(f, api.Clock.GetUtcNow().UtcDateTime);

        // An upload may still be writing its row: too fresh to touch.
        Assert.Equal(0, await Run<MemoryPurgeJob>(api, j => j.SweepOrphansAsync()));
        api.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, await Run<MemoryPurgeJob>(api, j => j.SweepOrphansAsync()));

        Assert.Equal(["2560.jpg"], Files(api.PhotoRoot));
        Assert.Empty(Files(api.ArchiveRoot));
        await using var survivor = await store.OpenAsync(PhotoKeys.File(KeptCouple(api), kept.Id, 2560));
        Assert.NotNull(survivor);
    }
}
