using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ailu.Api.Features.Gifts;

public sealed record RedeemRequest(string? Code);
public sealed record RedeemDto(string Kind, string Tier, DateTimeOffset PeriodEnd);
public sealed record PromoRequest(string? Code, string? Tier, int Months, int Days, int? MaxRedemptions, DateTimeOffset? ValidUntil, string? Note);
public sealed record PromoDto(string Code, string Tier, int Months, int Days, int? MaxRedemptions, int Redemptions, DateTimeOffset? ValidUntil,
    string? Note, bool Disabled);
public sealed record GiftCodeRequest(string? Tier, int Months, string? Note);
public sealed record GiftCodeDto(string Code, string Tier, int Months, bool Redeemed, string? Note, DateTimeOffset CreatedAt);

public static class GiftEndpoints
{
    // Couples enter codes in the app; the team makes them. Gifts are sold person to person, outside the app
    // (App Store and Google Play rules allow only their own payments for a plan bought in the app), so the
    // app has no gift checkout: once the buyer has paid, the team makes a gift code here and hands it over.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/billing/redeem", Redeem).RequireAuthorization().WithTags("Billing");

        var admin = app.MapGroup("/v1/admin").WithTags("Admin");
        admin.MapGet("/promo-codes", ListPromos);
        admin.MapPost("/promo-codes", CreatePromo);
        admin.MapGet("/gift-codes", ListGifts);
        admin.MapPost("/gift-codes", CreateGift);
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
        if (!AdminKey.Allows(http, options)) return Results.Unauthorized();
        var codes = await db.PlanCodes.Where(c => c.Kind == PlanCodeKind.Promo).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        return Results.Ok(codes.Select(ToPromo).ToList());
    }

    private static async Task<IResult> CreatePromo(PromoRequest body, HttpContext http, GiftService gifts, BillingOptions options,
        TimeProvider clock, CancellationToken ct)
    {
        if (!AdminKey.Allows(http, options)) return Results.Unauthorized();
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

    private static async Task<IResult> ListGifts(HttpContext http, AiluDb db, BillingOptions options, CancellationToken ct)
    {
        if (!AdminKey.Allows(http, options)) return Results.Unauthorized();
        var codes = await db.PlanCodes.Where(c => c.Kind == PlanCodeKind.Gift).OrderByDescending(c => c.CreatedAt).Take(200).ToListAsync(ct);
        return Results.Ok(codes.Select(ToGift).ToList());
    }

    private static async Task<IResult> CreateGift(GiftCodeRequest body, HttpContext http, GiftService gifts, BillingOptions options,
        CancellationToken ct)
    {
        if (!AdminKey.Allows(http, options)) return Results.Unauthorized();
        if (body.Tier is not ("plus" or "premium")) return Errors.Validation("tier", "Тариф: plus или premium.");
        if (body.Months is < 1 or > 24) return Errors.Validation("months", "Срок: от 1 до 24 месяцев.");
        if (body.Note?.Length > 200) return Errors.Validation("note", "Заметка до 200 символов.");
        var code = await gifts.CreateGiftAsync(body.Tier == "plus" ? CouplePlan.Plus : CouplePlan.Premium, body.Months, body.Note, ct);
        return Results.Ok(ToGift(code));
    }

    private static GiftCodeDto ToGift(PlanCode c) =>
        new(PlanCodes.Display(c.Code, c.Kind), Lower(c.Tier), c.Months, c.Redemptions > 0, c.Note, c.CreatedAt);

    private static PromoDto ToPromo(PlanCode c) =>
        new(c.Code, Lower(c.Tier), c.Months, c.Days, c.MaxRedemptions, c.Redemptions, c.ValidUntil, c.Note, c.DisabledAt is not null);

    private static string Lower<T>(T value) where T : Enum => value.ToString().ToLowerInvariant();
}
