using System.Security.Cryptography;
using System.Text;
using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ailu.Api.Features.Gifts;

public sealed record GiftProductDto(string Id, string Tier, int Months, int PriceKzt);
public sealed record GiftOrderDto(Guid Id, string ProductId, string Tier, int Months, int PriceKzt, string Status, string? Code,
    string? PaymentUrl, bool Redeemed, DateTimeOffset CreatedAt);
public sealed record GiftsDto(bool Available, IReadOnlyList<GiftProductDto> Products, IReadOnlyList<GiftOrderDto> Orders);
public sealed record GiftOrderRequest(string? ProductId);
public sealed record CheckoutNotification(string? CheckoutId);
public sealed record RedeemRequest(string? Code);
public sealed record RedeemDto(string Kind, string Tier, DateTimeOffset PeriodEnd);
public sealed record PromoRequest(string? Code, string? Tier, int Months, int Days, int? MaxRedemptions, DateTimeOffset? ValidUntil, string? Note);
public sealed record PromoDto(string Code, string Tier, int Months, int Days, int? MaxRedemptions, int Redemptions, DateTimeOffset? ValidUntil,
    string? Note, bool Disabled);

public static class GiftEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/gifts").WithTags("Gifts");
        g.MapGet("/", List).RequireAuthorization();
        g.MapPost("/", Order).RequireAuthorization();
        g.MapGet("/{id:guid}", Get).RequireAuthorization();
        // The payment provider's notification. Unauthenticated by nature: it only makes us re-ask the provider.
        g.MapPost("/webhooks/checkout", Webhook);

        app.MapPost("/v1/billing/redeem", Redeem).RequireAuthorization().WithTags("Billing");

        var admin = app.MapGroup("/v1/admin/promo-codes").WithTags("Admin");
        admin.MapGet("/", ListPromos);
        admin.MapPost("/", CreatePromo);
    }

    private static async Task<IResult> List(HttpContext http, AiluDb db, GiftService gifts, BillingOptions options, CancellationToken ct)
    {
        var me = CurrentUser.Id(http.User);
        var orders = await db.GiftOrders.Where(o => o.BuyerUserId == me).OrderByDescending(o => o.CreatedAt).Take(50).ToListAsync(ct);
        // Payments still open are asked about again, in case a notification was lost.
        foreach (var order in orders.Where(o => o.Status == GiftOrderStatus.Pending))
        {
            try
            {
                await gifts.RefreshAsync(order, ct);
            }
            catch (StoreUnavailableException)
            {
                // Shown as still waiting; the next look asks again.
            }
        }
        return Results.Ok(new GiftsDto(gifts.Available,
            gifts.Available ? options.Gifts.Select(p => new GiftProductDto(p.Id, Lower(p.Tier), p.Months, p.PriceKzt)).ToList() : [],
            await ToDtos(db, orders, ct)));
    }

    private static async Task<IResult> Order(GiftOrderRequest body, HttpContext http, AiluDb db, GiftService gifts, CancellationToken ct)
    {
        if (body.ProductId is null || gifts.Product(body.ProductId) is not { } product)
            return Errors.Validation("productId", "Такого подарка нет.");
        if (!gifts.Available) return GiftsUnavailable();
        GiftOrder order;
        try
        {
            order = await gifts.OrderAsync(CurrentUser.Id(http.User), product, ct);
        }
        catch (StoreUnavailableException)
        {
            return GiftsUnavailable();
        }
        return Results.Ok((await ToDtos(db, [order], ct))[0]);
    }

    private static async Task<IResult> Get(Guid id, HttpContext http, AiluDb db, GiftService gifts, CancellationToken ct)
    {
        var me = CurrentUser.Id(http.User);
        var order = await db.GiftOrders.SingleOrDefaultAsync(o => o.Id == id && o.BuyerUserId == me, ct);
        if (order is null) return Errors.NotFound("gift_not_found", "Такого подарка нет.");
        if (order.Status == GiftOrderStatus.Pending)
        {
            try
            {
                await gifts.RefreshAsync(order, ct);
            }
            catch (StoreUnavailableException)
            {
                return GiftsUnavailable();
            }
        }
        return Results.Ok((await ToDtos(db, [order], ct))[0]);
    }

    private static async Task<IResult> Webhook(CheckoutNotification body, string? secret, GiftService gifts, BillingOptions options,
        CancellationToken ct)
    {
        var expected = options.GiftWebhookSecret;
        if (expected == "" || secret is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(expected)))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(body.CheckoutId)) return Results.Ok();
        try
        {
            await gifts.RefreshAsync(body.CheckoutId, ct);
        }
        catch (StoreUnavailableException)
        {
            // A provider retries a notification that didn't get a 2xx.
            return GiftsUnavailable();
        }
        return Results.Ok();
    }

    private static async Task<IResult> Redeem(RedeemRequest body, HttpContext http, AiluDb db, GiftService gifts, CancellationToken ct)
    {
        var code = PlanCodes.Normalize(body.Code ?? "");
        if (!PlanCodes.IsValid(code)) return Errors.Validation("code", "Введите код из подарка или промокод.");
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();

        var result = await gifts.RedeemAsync(scope, code, ct);
        return result.Result switch
        {
            RedeemResult.NotFound => Errors.NotFound("plan_code_not_found", "Такого кода нет. Проверьте, нет ли в нём опечатки."),
            RedeemResult.Used => Errors.Conflict("plan_code_used", result.Code!.Kind == PlanCodeKind.Gift
                ? "Этот подарок уже активирован."
                : "Ваша пара уже использовала этот промокод."),
            RedeemResult.Ended => Errors.Conflict("plan_code_ended", "Срок этого промокода закончился."),
            _ => Results.Ok(new RedeemDto(Lower(result.Code!.Kind), Lower(result.Subscription!.Tier), result.Subscription.PeriodEnd))
        };
    }

    private static async Task<IResult> ListPromos(HttpContext http, AiluDb db, BillingOptions options, CancellationToken ct)
    {
        if (!IsAdmin(http, options)) return Results.Unauthorized();
        var codes = await db.PlanCodes.Where(c => c.Kind == PlanCodeKind.Promo).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        return Results.Ok(codes.Select(ToPromo).ToList());
    }

    private static async Task<IResult> CreatePromo(PromoRequest body, HttpContext http, GiftService gifts, BillingOptions options,
        TimeProvider clock, CancellationToken ct)
    {
        if (!IsAdmin(http, options)) return Results.Unauthorized();
        var code = PlanCodes.Normalize(body.Code ?? "");
        if (!PlanCodes.IsValid(code)) return Errors.Validation("code", "Код от 4 до 32 букв и цифр.");
        if (body.Tier is not ("plus" or "premium")) return Errors.Validation("tier", "Тариф: plus или premium.");
        if (body.Months is < 0 or > 24 || body.Days is < 0 or > 366 || body.Months + body.Days == 0)
            return Errors.Validation("months", "Срок: до 24 месяцев или до 366 дней.");
        if (body.MaxRedemptions is < 1) return Errors.Validation("maxRedemptions", "Хотя бы одна пара.");
        if (body.ValidUntil <= clock.GetUtcNow()) return Errors.Validation("validUntil", "Дата окончания уже прошла.");
        if (body.Note?.Length > 200) return Errors.Validation("note", "Заметка до 200 символов.");

        try
        {
            var created = await gifts.CreatePromoAsync(code, body.Tier == "plus" ? CouplePlan.Plus : CouplePlan.Premium, body.Months, body.Days,
                body.MaxRedemptions, body.ValidUntil, body.Note, ct);
            return Results.Ok(ToPromo(created));
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Errors.Conflict("plan_code_taken", "Такой код уже есть.");
        }
    }

    private static bool IsAdmin(HttpContext http, BillingOptions options)
    {
        var expected = options.AdminKey;
        var given = http.Request.Headers["X-Admin-Key"].ToString();
        return expected != "" && given != ""
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
    }

    private static IResult GiftsUnavailable() =>
        Errors.Unavailable("gifts_unavailable", "Подарочные подписки пока недоступны. Попробуйте чуть позже.");

    private static async Task<List<GiftOrderDto>> ToDtos(AiluDb db, IReadOnlyList<GiftOrder> orders, CancellationToken ct)
    {
        var ids = orders.Where(o => o.CodeId is not null).Select(o => o.CodeId!.Value).ToList();
        var codes = await db.PlanCodes.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        return orders.Select(o =>
        {
            var code = o.CodeId is { } id ? codes.GetValueOrDefault(id) : null;
            // The buyer sees whether the gift was entered, never by whom.
            return new GiftOrderDto(o.Id, o.ProductId, Lower(o.Tier), o.Months, o.PriceKzt, Lower(o.Status),
                code is null || code.DisabledAt is not null ? null : PlanCodes.Display(code.Code, code.Kind),
                o.Status == GiftOrderStatus.Pending ? o.PaymentUrl : null, code?.Redemptions > 0, o.CreatedAt);
        }).ToList();
    }

    private static PromoDto ToPromo(PlanCode c) =>
        new(c.Code, Lower(c.Tier), c.Months, c.Days, c.MaxRedemptions, c.Redemptions, c.ValidUntil, c.Note, c.DisabledAt is not null);

    private static string Lower<T>(T value) where T : Enum => value.ToString().ToLowerInvariant();
}
