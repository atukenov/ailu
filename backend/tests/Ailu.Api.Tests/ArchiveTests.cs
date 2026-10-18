using System.Net;
using System.Net.Http.Json;
using Ailu.Api.Features.Memories;
using Ailu.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Ailu.Api.Tests;

public class ArchiveTests
{
    [Fact]
    public async Task After_a_couple_ends_both_read_its_memories_and_the_partners_photos_for_30_days()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var mine = await StorageTests.MemoryWithPhoto(a, "Рим");
        var theirs = await StorageTests.MemoryWithPhoto(b, "Алматы");

        Assert.Empty((await a.GetFromJsonAsync<List<PastCoupleDto>>("/v1/archive"))!);
        (await a.DeleteAsync("/v1/couple")).EnsureSuccessStatusCode();

        var past = (await b.GetFromJsonAsync<List<PastCoupleDto>>("/v1/archive"))!.Single();
        Assert.Equal(("Алмаз", 2), (past.Partner?.Name, past.Memories));
        var url = $"/v1/archive/{past.Id}/memories";

        var page = (await b.GetFromJsonAsync<MemoryPage>(url))!;
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, m => Assert.Single(m.Photos));
        (await api.CreateClient().GetAsync(page.Items.Single(m => m.Id == mine.Id).Photos[0].Thumb)).EnsureSuccessStatusCode();

        // A month on, each still sees every memory, but only their own photos.
        api.Clock.Advance(TimeSpan.FromDays(31));
        a = await api.SignInAsync("+77010000001");
        b = await api.SignInAsync("+77010000002");
        var later = (await b.GetFromJsonAsync<MemoryPage>(url))!;
        Assert.Empty(later.Items.Single(m => m.Id == mine.Id).Photos);
        Assert.Single(later.Items.Single(m => m.Id == theirs.Id).Photos);
        var forA = (await a.GetFromJsonAsync<MemoryDto>($"{url}/{mine.Id}"))!;
        Assert.True(forA.Mine);
        Assert.Single(forA.Photos);
    }

    [Fact]
    public async Task Only_the_author_deletes_from_the_archive_and_strangers_see_nothing()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var memory = await StorageTests.MemoryWithPhoto(a);
        (await b.DeleteAsync("/v1/couple")).EnsureSuccessStatusCode();
        var id = (await a.GetFromJsonAsync<List<PastCoupleDto>>("/v1/archive"))!.Single().Id;
        var url = $"/v1/archive/{id}/memories/{memory.Id}";

        var stranger = await api.SignInAsync("+77010000009", "Дана");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/v1/archive/{id}/memories")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync(url)).StatusCode);

        // The deleted memory goes the usual way: its file is gone after the bin.
        api.Clock.Advance(TimeSpan.FromDays(31));
        using var scope = api.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<MemoryPurgeJob>().RunOnceAsync());
        Assert.Empty(Directory.GetFiles(api.PhotoRoot, "*.jpg", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_new_couple_does_not_hide_the_old_archive()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await StorageTests.MemoryWithPhoto(a);
        (await a.DeleteAsync("/v1/couple")).EnsureSuccessStatusCode();

        var c = await api.SignInAsync("+77010000003", "Дана");
        (await a.PostAsJsonAsync("/v1/couple/invitations", new { phone = "+77010000003" })).EnsureSuccessStatusCode();
        (await c.PostAsync($"/v1/couple/invitations/{api.Sms.LastCode("+77010000003")}/accept", null)).EnsureSuccessStatusCode();

        var past = (await a.GetFromJsonAsync<List<PastCoupleDto>>("/v1/archive"))!.Single();
        Assert.Equal("Нурай", past.Partner?.Name);
        Assert.Empty((await c.GetFromJsonAsync<List<PastCoupleDto>>("/v1/archive"))!);
        // The current couple's memories are not part of the archive.
        Assert.Empty((await a.GetFromJsonAsync<MemoryPage>("/v1/couple/memories"))!.Items);
    }
}
