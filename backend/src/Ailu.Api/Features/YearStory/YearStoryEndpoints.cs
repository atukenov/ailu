using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ailu.Api.Features.YearStory;

public sealed record YearStoryAppleClaim(string? TransactionId);
public sealed record YearStoryGoogleClaim(string? PurchaseToken);

// A slide as the app shows it in the preview: the plan's slide with its photo as a signed URL.
public sealed record StorySlideDto(string Kind, int DurationMs, DateOnly? Date, Guid? MemoryId, string? Photo, string? Title,
    string? Place, int? Days, int? Years, string? Icon, string? Slug, StoryStats? Stats);

// Status is "making" until the film is ready, then "ready" with a download link; "failed" means the
// renderer gave up and someone has to look.
public sealed record YearStoryOrderDto(string Status, DateTimeOffset OrderedAt, bool PaidByMe, DateTimeOffset? ReadyAt, string? Video);

public sealed record YearStoryDto(int Year, IReadOnlyList<int> Years, bool Open, DateOnly OpensOn, int PriceKzt,
    string AppleProductId, string GoogleProductId, int Memories, int MinMemories, int DurationMs,
    IReadOnlyList<StorySlideDto> Slides, YearStoryOrderDto? Order);

// The video story of the year (roadmap phase 4, P1, 1 990 ₸): a one-minute film from the year's best
// memories and dates. Anyone can watch the preview as it grows during the year; from 1 December either
// partner can buy it, once per year, and both get the film. It is a one-time store purchase, checked
// with the store like a subscription, and is open on every plan.
public static class YearStoryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/couple/year-story").WithTags("YearStory").RequireAuthorization();
        g.MapGet("", Get);
        g.MapPost("/{year:int}/apple", (int year, YearStoryAppleClaim body, HttpContext http, AiluDb db, YearStoryService stories,
                IEnumerable<IStoreClient> stores, BillingOptions options, PhotoUrls urls, TimeProvider clock, ILogger<YearStoryDto> log,
                CancellationToken ct) =>
            Order(StoreProvider.Apple, year, body.TransactionId, "transactionId", http, db, stories, stores, options, urls, clock, log, ct));
        g.MapPost("/{year:int}/google", (int year, YearStoryGoogleClaim body, HttpContext http, AiluDb db, YearStoryService stories,
                IEnumerable<IStoreClient> stores, BillingOptions options, PhotoUrls urls, TimeProvider clock, ILogger<YearStoryDto> log,
                CancellationToken ct) =>
            Order(StoreProvider.Google, year, body.PurchaseToken, "purchaseToken", http, db, stories, stores, options, urls, clock, log, ct));

        // Anonymous: the signature in the URL is the permission, like photos.
        app.MapGet("/v1/stories/{id:guid}/video", Video).WithTags("YearStory");
    }

    private static async Task<IResult> Get(int? year, HttpContext http, AiluDb db, YearStoryService stories, BillingOptions options,
        PhotoUrls urls, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var today = CoupleCalendar.Today(scope.Couple, clock);
        var y = year ?? YearStoryService.DefaultYear(scope.Couple, today);
        if (!YearStoryService.Years(scope.Couple, today).Contains(y)) return Errors.Validation("year", "Вас ещё не было вместе в этом году.");
        return Results.Ok(await Load(scope, y, db, stories, options, urls, clock, ct));
    }

    // The app buys the product from the store, then hands the purchase here. The store decides whether it
    // was paid for; the order is taken only for an open year that has no film yet. The app checks both
    // before it lets anyone pay, so a refusal here means both partners bought at the same moment.
    private static async Task<IResult> Order(StoreProvider provider, int year, string? storeId, string field, HttpContext http,
        AiluDb db, YearStoryService stories, IEnumerable<IStoreClient> stores, BillingOptions options, PhotoUrls urls, TimeProvider clock,
        ILogger log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storeId) || storeId.Length > 500) return Errors.Validation(field, "Нет данных о покупке.");
        storeId = storeId.Trim();
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var today = CoupleCalendar.Today(scope.Couple, clock);
        if (!YearStoryService.Years(scope.Couple, today).Contains(year) || !YearStoryService.IsOpen(year, today))
            return Errors.Conflict("story_not_open", "Историю этого года можно будет заказать с 1 декабря.");

        var existing = await db.YearStories.AsNoTracking().SingleOrDefaultAsync(s => s.CoupleId == scope.Couple.Id && s.Year == year, ct);
        if (existing is not null)
            return existing.Provider == provider && existing.StoreId == storeId
                ? Results.Ok(await Load(scope, year, db, stories, options, urls, clock, ct))
                : AlreadyOrdered(log, scope, year, storeId);

        var store = stores.Single(s => s.Provider == provider);
        var product = options.YearStory;
        StoreProductPurchase? purchase;
        try
        {
            purchase = await store.FetchProductAsync(product.StoreProductId(provider), storeId, ct);
        }
        catch (StoreUnavailableException)
        {
            return Errors.Unavailable("store_unavailable", "Не получилось связаться с магазином. Попробуйте чуть позже.");
        }
        if (purchase is not { Paid: true }) return Errors.NotFound("purchase_not_found", "Магазин не нашёл эту покупку.");
        if (purchase.AccountId is { Length: > 0 } account && !string.Equals(account, scope.UserId.ToString(), StringComparison.OrdinalIgnoreCase))
            return Errors.Conflict("purchase_other_account", "Эта покупка сделана в другом аккаунте Ailu.");
        if (await db.YearStories.AnyAsync(s => s.Provider == provider && s.StoreId == purchase.StoreId, ct))
            return Errors.Conflict("purchase_other_account", "Эта покупка уже оплатила другую историю.");

        var locale = scope.Me.User?.Locale ?? "ru";
        var plan = await stories.BuildAsync(scope.Couple, year, locale, ct);
        var now = clock.GetUtcNow();
        db.YearStories.Add(new Data.YearStory
        {
            Id = Guid.NewGuid(), CoupleId = scope.Couple.Id, Year = year, PayerUserId = scope.UserId, Provider = provider,
            StoreId = purchase.StoreId, ProductId = purchase.ProductId, Sandbox = purchase.Sandbox, Locale = locale,
            Storyboard = YearStoryService.Serialize(plan.Slides), Status = YearStoryStatus.Queued, NextAttemptAt = now, OrderedAt = now
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The partner's order for the same year, or this one sent twice, landed first.
            db.ChangeTracker.Clear();
            var won = await db.YearStories.AsNoTracking().SingleOrDefaultAsync(s => s.CoupleId == scope.Couple.Id && s.Year == year, ct);
            if (won is null || won.StoreId != purchase.StoreId) return AlreadyOrdered(log, scope, year, storeId);
        }

        if (purchase.NeedsAcknowledge)
        {
            try
            {
                await store.AcknowledgeProductAsync(purchase, ct);
            }
            catch (StoreUnavailableException e)
            {
                // The app consumes the purchase after this answer, which acknowledges it too.
                log.LogWarning(e, "Could not acknowledge year story purchase for couple {Couple}", scope.Couple.Id);
            }
        }
        return Results.Ok(await Load(scope, year, db, stories, options, urls, clock, ct));
    }

    // Someone paid for a film the couple already has. The store has the money, so support refunds it.
    private static IResult AlreadyOrdered(ILogger log, CoupleScope scope, int year, string storeId)
    {
        log.LogWarning("Couple {Couple} paid again for the {Year} story ({StoreId}); needs a refund", scope.Couple.Id, year, storeId);
        return Errors.Conflict("story_ordered", "История этого года уже заказана. Напишите нам, и мы вернём деньги за вторую покупку.");
    }

    private static async Task<IResult> Video(Guid id, long exp, string? sig, AiluDb db, IPhotoStore store, PhotoUrls urls, CancellationToken ct)
    {
        if (!urls.IsValidVideo(id, exp, sig)) return Results.NotFound();
        var story = await db.YearStories.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.Status == YearStoryStatus.Ready, ct);
        if (story is null) return Results.NotFound();
        var stream = await store.OpenAsync(PhotoKeys.Story(story.CoupleId, story.Id), ct);
        return stream is null
            ? Results.NotFound()
            : Results.Stream(stream, "video/mp4", $"ailu-{story.Year}.mp4", enableRangeProcessing: true);
    }

    private static async Task<YearStoryDto> Load(CoupleScope scope, int year, AiluDb db, YearStoryService stories, BillingOptions options,
        PhotoUrls urls, TimeProvider clock, CancellationToken ct)
    {
        var today = CoupleCalendar.Today(scope.Couple, clock);
        var locale = scope.Me.User?.Locale;
        var order = await db.YearStories.AsNoTracking().SingleOrDefaultAsync(s => s.CoupleId == scope.Couple.Id && s.Year == year, ct);

        List<StorySlide> slides;
        int memories;
        if (order is null)
        {
            var plan = await stories.BuildAsync(scope.Couple, year, locale, ct);
            (slides, memories) = (plan.Slides, plan.Memories);
        }
        else
        {
            // What was paid for, less any memory deleted since (the film leaves those out too).
            slides = YearStoryService.Deserialize(order.Storyboard);
            var ids = slides.Where(s => s.MemoryId is not null).Select(s => s.MemoryId!.Value).ToList();
            var live = await db.Memories.Where(m => ids.Contains(m.Id) && m.DeletedAt == null).Select(m => m.Id).ToListAsync(ct);
            slides = slides.Where(s => s.Kind != StorySlideKind.Memory || live.Contains(s.MemoryId!.Value)).ToList();
            memories = slides.Count(s => s.Kind == StorySlideKind.Memory);
        }

        // Achievement titles are library text: each partner reads them in their own language.
        var slugs = slides.Where(s => s.Slug is not null).Select(s => s.Slug!).ToList();
        var titles = slugs.Count == 0
            ? new Dictionary<string, string>()
            : await db.Achievements.AsNoTracking().Where(a => slugs.Contains(a.Slug)).ToDictionaryAsync(a => a.Slug, a => a.TitleIn(locale), ct);

        var product = options.YearStory;
        return new YearStoryDto(year, YearStoryService.Years(scope.Couple, today), YearStoryService.IsOpen(year, today),
            YearStoryService.OpensOn(year), product.PriceKzt, product.AppleProductId, product.GoogleProductId, memories,
            YearStoryPlan.MinMemories, YearStoryPlan.DurationMs(slides),
            slides.Select(s => new StorySlideDto(s.Kind, s.DurationMs, s.Date, s.MemoryId,
                s.PhotoId is { } photo ? urls.For(photo, PhotoProcessor.Screen) : null,
                s.Slug is not null && titles.TryGetValue(s.Slug, out var t) ? t : s.Title,
                s.Place, s.Days, s.Years, s.Icon, s.Slug, s.Stats)).ToList(),
            order is null
                ? null
                : new YearStoryOrderDto(order.Status == YearStoryStatus.Queued ? "making" : order.Status.ToString().ToLowerInvariant(),
                    order.OrderedAt, order.PayerUserId == scope.UserId, order.ReadyAt,
                    order.Status == YearStoryStatus.Ready ? urls.ForVideo(order.Id) : null));
    }
}
