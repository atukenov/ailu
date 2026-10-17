using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Features.Billing;

public sealed record AppleClaim(string? TransactionId);
public sealed record GoogleClaim(string? PurchaseToken);
public sealed record AppleNotification(string? SignedPayload);
public sealed record ProductDto(string Id, string Tier, string Period, int PriceKzt, string AppleProductId, string GoogleProductId,
    string GoogleBasePlanId);
public sealed record SubscriptionDto(string Tier, string Provider, string Status, DateTimeOffset PeriodEnd, bool AutoRenew, bool PaidByMe);
public sealed record BillingDto(string Plan, SubscriptionDto? Subscription, bool TrialAvailable, int TrialDays, IReadOnlyList<ProductDto> Products);

public static class BillingEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/billing").WithTags("Billing");
        g.MapGet("/", Get).RequireAuthorization();
        g.MapPost("/apple", ClaimApple).RequireAuthorization();
        g.MapPost("/google", ClaimGoogle).RequireAuthorization();
        g.MapPost("/trial", StartTrial).RequireAuthorization();

        // Store server notifications. Unauthenticated by nature: they only make us re-ask the store.
        g.MapPost("/webhooks/apple", AppleWebhook);
        g.MapPost("/webhooks/google", GoogleWebhook);
    }

    private static async Task<IResult> Get(HttpContext http, AiluDb db, BillingService billing, BillingOptions options, TimeProvider clock,
        CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        return scope is null ? Errors.NoCouple() : Results.Ok(await ToDto(scope, db, billing, options, clock, ct));
    }

    private static Task<IResult> ClaimApple(AppleClaim body, HttpContext http, AiluDb db, BillingService billing, BillingOptions options,
        TimeProvider clock, CancellationToken ct) =>
        Claim(StoreProvider.Apple, body.TransactionId, "transactionId", http, db, billing, options, clock, ct);

    private static Task<IResult> ClaimGoogle(GoogleClaim body, HttpContext http, AiluDb db, BillingService billing, BillingOptions options,
        TimeProvider clock, CancellationToken ct) =>
        Claim(StoreProvider.Google, body.PurchaseToken, "purchaseToken", http, db, billing, options, clock, ct);

    private static async Task<IResult> Claim(StoreProvider provider, string? storeId, string field, HttpContext http, AiluDb db,
        BillingService billing, BillingOptions options, TimeProvider clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storeId) || storeId.Length > 500) return Errors.Validation(field, "Нет данных о покупке.");
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();

        ClaimResult result;
        try
        {
            result = await billing.ClaimAsync(scope, provider, storeId.Trim(), ct);
        }
        catch (StoreUnavailableException)
        {
            return StoreDown();
        }

        return result switch
        {
            ClaimResult.NotFound => Errors.NotFound("purchase_not_found", "Магазин не нашёл эту покупку."),
            ClaimResult.UnknownProduct => Errors.NotFound("purchase_not_found", "Эта покупка не относится к Ailu."),
            ClaimResult.OtherOwner => Errors.Conflict("purchase_other_account",
                "Эта подписка оформлена в другом аккаунте Ailu. Войдите в него, чтобы её восстановить."),
            _ => Results.Ok(await ToDto(scope, db, billing, options, clock, ct))
        };
    }

    private static async Task<IResult> StartTrial(HttpContext http, AiluDb db, BillingService billing, BillingOptions options,
        TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        if (!await billing.StartTrialAsync(scope, ct))
            return Errors.Conflict("trial_used", "Пробный период уже был у вашей пары.");
        return Results.Ok(await ToDto(scope, db, billing, options, clock, ct));
    }

    private static async Task<IResult> AppleWebhook(AppleNotification body, BillingService billing, CancellationToken ct)
    {
        if (body.SignedPayload is null || AppleStoreClient.OriginalTransactionIdOf(body.SignedPayload) is not { } id) return Results.Ok();
        try
        {
            await billing.RefreshAsync(StoreProvider.Apple, id, ct);
        }
        catch (StoreUnavailableException)
        {
            // Apple retries a notification that didn't get a 200.
            return StoreDown();
        }
        return Results.Ok();
    }

    private static async Task<IResult> GoogleWebhook(JsonElement body, string? secret, BillingService billing, BillingOptions options,
        CancellationToken ct)
    {
        var expected = options.Google.WebhookSecret;
        if (expected == "" || secret is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(expected)))
            return Results.Unauthorized();
        if (GoogleStoreClient.PurchaseTokenOf(body) is not { } token) return Results.Ok();
        try
        {
            await billing.RefreshAsync(StoreProvider.Google, token, ct);
        }
        catch (StoreUnavailableException)
        {
            // Pub/Sub redelivers a push that didn't get a 2xx.
            return StoreDown();
        }
        return Results.Ok();
    }

    private static IResult StoreDown() =>
        Errors.Unavailable("store_unavailable", "Не получилось связаться с магазином. Попробуйте чуть позже.");

    private static async Task<BillingDto> ToDto(CoupleScope scope, AiluDb db, BillingService billing, BillingOptions options,
        TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // The subscription behind the couple's plan: the best tier, then the one that runs longest.
        var live = await db.Subscriptions.Where(s => s.CoupleId == scope.Couple.Id
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Grace) && s.PeriodEnd > now)
            .ToListAsync(ct);
        // Read fresh: the plan was just changed by an UPDATE the tracked couple doesn't know about.
        var plan = await db.Couples.Where(c => c.Id == scope.Couple.Id).Select(c => c.Plan).SingleAsync(ct);
        var current = live.OrderByDescending(s => s.Tier).ThenByDescending(s => s.PeriodEnd).FirstOrDefault();

        return new BillingDto(
            plan.ToString().ToLowerInvariant(),
            current is null
                ? null
                : new SubscriptionDto(current.Tier.ToString().ToLowerInvariant(), current.Provider.ToString().ToLowerInvariant(),
                    current.Status.ToString().ToLowerInvariant(), current.PeriodEnd, current.AutoRenew, current.PayerUserId == scope.UserId),
            await billing.TrialAvailableAsync(scope, ct),
            BillingOptions.TrialDays,
            options.Products.Select(p => new ProductDto(p.Id, p.Tier.ToString().ToLowerInvariant(), p.Period, p.PriceKzt,
                p.AppleProductId, p.GoogleProductId, p.GoogleBasePlanId)).ToList());
    }
}
