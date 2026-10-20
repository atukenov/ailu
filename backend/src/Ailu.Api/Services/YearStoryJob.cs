using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

// Works through the queue of ordered films: renders each, keeps it with the couple's photos and tells both
// partners it is ready. Orders wait in the queue while no renderer is configured, and a failing render is
// tried again later, a few times, before someone has to look at it.
public sealed class YearStoryJob(AiluDb db, IStoryRenderer renderer, IPhotoStore store, IPushSender push, TimeProvider clock,
    ILogger<YearStoryJob> log)
{
    public const int MaxAttempts = 5;

    // An order is claimed for this long, so two instances never render the same film at once.
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(30);

    // Without a renderer, look again in an hour.
    public static readonly TimeSpan WaitForRenderer = TimeSpan.FromHours(1);

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var due = await db.YearStories.AsNoTracking()
            .Where(s => s.Status == YearStoryStatus.Queued && s.NextAttemptAt <= now)
            .OrderBy(s => s.NextAttemptAt).Take(5).ToListAsync(ct);

        var done = 0;
        foreach (var story in due)
        {
            var claimed = await db.YearStories
                .Where(s => s.Id == story.Id && s.Status == YearStoryStatus.Queued && s.NextAttemptAt == story.NextAttemptAt)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.NextAttemptAt, now + Lease), ct);
            if (claimed == 0) continue;
            if (await RenderAsync(story, ct)) done++;
        }
        return done;
    }

    private async Task<bool> RenderAsync(YearStory story, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var couple = await db.Couples.AsNoTracking().Include(c => c.Members).ThenInclude(m => m.User)
            .SingleAsync(c => c.Id == story.CoupleId, ct);

        byte[] video;
        try
        {
            video = await renderer.RenderAsync(await RequestAsync(story, couple, ct), ct);
        }
        catch (RendererUnavailableException e)
        {
            log.LogWarning("Year story {Story} waits for a renderer: {Reason}", story.Id, e.Message);
            await db.YearStories.Where(s => s.Id == story.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.NextAttemptAt, now + WaitForRenderer), ct);
            return false;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var attempts = story.Attempts + 1;
            var failed = attempts >= MaxAttempts;
            log.LogError(e, "Rendering year story {Story} failed (attempt {Attempt})", story.Id, attempts);
            var error = e.Message.Length > 500 ? e.Message[..500] : e.Message;
            await db.YearStories.Where(s => s.Id == story.Id).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Attempts, attempts)
                .SetProperty(s => s.Error, error)
                .SetProperty(s => s.Status, failed ? YearStoryStatus.Failed : YearStoryStatus.Queued)
                .SetProperty(s => s.NextAttemptAt, now + TimeSpan.FromMinutes(10 * Math.Pow(2, attempts))), ct);
            return false;
        }

        await store.PutAsync(PhotoKeys.Story(story.CoupleId, story.Id), video, StorageTier.Standard, ct);
        await db.YearStories.Where(s => s.Id == story.Id).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.Status, YearStoryStatus.Ready)
            .SetProperty(s => s.VideoBytes, video.LongLength)
            .SetProperty(s => s.ReadyAt, now)
            .SetProperty(s => s.Error, (string?)null), ct);
        await NotifyAsync(story, couple, ct);
        return true;
    }

    private async Task<StoryRenderRequest> RequestAsync(YearStory story, Couple couple, CancellationToken ct)
    {
        var slides = YearStoryService.Deserialize(story.Storyboard);
        var photoIds = slides.Where(s => s.PhotoId is not null).Select(s => s.PhotoId!.Value).ToList();

        // A memory deleted since the order leaves the film; the rest keep their time.
        var live = await db.MemoryPhotos.AsNoTracking()
            .Where(p => photoIds.Contains(p.Id) && db.Memories.Any(m => m.Id == p.MemoryId && m.DeletedAt == null))
            .Select(p => p.Id).ToListAsync(ct);
        slides = slides.Where(s => s.Kind != StorySlideKind.Memory || (s.PhotoId is { } id && live.Contains(id))).ToList();

        var members = couple.Members.OrderBy(m => m.Role).ToList();
        string NameOf(int i) => i < members.Count ? members[i].User?.Name ?? "" : "";
        return new StoryRenderRequest(story.Id, story.CoupleId, story.Year, story.Locale, NameOf(0), NameOf(1), slides,
            live.ToDictionary(id => id, id => PhotoKeys.File(story.CoupleId, id, PhotoProcessor.Full)));
    }

    private async Task NotifyAsync(YearStory story, Couple couple, CancellationToken ct)
    {
        var people = couple.Members.Where(m => m.LeftAt == null).ToList();
        var ids = people.Select(m => m.UserId).ToList();
        var devices = await db.Devices.Where(d => ids.Contains(d.UserId)).ToListAsync(ct);
        var messages = devices.Select(d =>
        {
            var kk = people.Single(m => m.UserId == d.UserId).User?.Locale == Locales.Kazakh;
            return new PushMessage(d.PushToken,
                kk ? $"{story.Year} жылдың бейне-әңгімесі дайын" : $"Видео-история {story.Year} года готова",
                kk ? "Бір минуттық фильм сіздің ортақ жылыңыз туралы. Ашып, сақтап алыңыз." : "Минутный фильм о вашем годе вместе. Откройте и сохраните его.");
        }).ToList();
        if (messages.Count == 0) return;
        try
        {
            await push.SendAsync(messages, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Year story {Story} push failed", story.Id);
        }
    }
}

public sealed class YearStoryScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<YearStoryScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<YearStoryJob>().RunOnceAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Year story run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
