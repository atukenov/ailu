using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Features.Auth;

public sealed record PhoneRequest(string Phone);
public sealed record PhoneVerify(string Phone, string Code);
public sealed record RefreshRequest(string RefreshToken);
public sealed record UserDto(Guid Id, string Phone, string? Name, string Locale, string TimeZone);
public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, UserDto User);
public sealed record UpdateMe(string? Name, string? Locale, string? TimeZone);

public static class AuthEndpoints
{
    private static readonly string[] Locales = ["kk", "ru", "en"];

    public static void Map(IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/v1/auth").WithTags("Auth");
        auth.MapPost("/phone/request", RequestCode);
        auth.MapPost("/phone/verify", Verify);
        auth.MapPost("/refresh", Refresh);

        var me = app.MapGroup("/v1/me").WithTags("Me").RequireAuthorization();
        me.MapGet("/", GetMe);
        me.MapPatch("/", PatchMe);
        me.MapDelete("/", DeleteMe);
    }

    public static UserDto ToDto(User u) => new(u.Id, u.Phone ?? "", u.Name, u.Locale, u.TimeZone);

    private static async Task<IResult> RequestCode(PhoneRequest body, AiluDb db, ISmsSender sms, AuthOptions opt,
        TimeProvider clock, CancellationToken ct)
    {
        var phone = PhoneNumber.Normalize(body.Phone);
        if (phone is null) return Errors.Validation("phone", "Нужен казахстанский номер в формате +7 7XX XXX XX XX.");

        var now = clock.GetUtcNow();
        var recent = await db.PhoneCodes.AnyAsync(c => c.Phone == phone && c.CreatedAt > now.AddSeconds(-opt.PhoneCodeResendSeconds), ct);
        if (recent) return Errors.TooManyRequests("Код уже отправлен. Повторить можно через минуту.");

        // The store reviewer's number gets its fixed code and no SMS; everything after that is the usual flow.
        var review = phone == opt.ReviewNumber;
        var code = review ? opt.ReviewCode : Codes.SixDigits();
        db.PhoneCodes.Add(new PhoneCode
        {
            Id = Guid.NewGuid(), Phone = phone, CodeHash = Codes.Hash(phone, code),
            CreatedAt = now, ExpiresAt = now.AddMinutes(opt.PhoneCodeMinutes)
        });
        await db.SaveChangesAsync(ct);
        if (!review) await sms.SendAsync(phone, $"Ailu: код входа {code}", ct);
        return Results.Accepted(value: new { phone, expiresInSeconds = opt.PhoneCodeMinutes * 60 });
    }

    private static async Task<IResult> Verify(PhoneVerify body, AiluDb db, AuthOptions opt, TokenIssuer tokens,
        DemoCouple demo, TimeProvider clock, CancellationToken ct)
    {
        var phone = PhoneNumber.Normalize(body.Phone);
        if (phone is null || string.IsNullOrWhiteSpace(body.Code)) return Errors.Validation("code", "Неверный код.");

        var now = clock.GetUtcNow();
        var pending = await db.PhoneCodes
            .Where(c => c.Phone == phone && c.ConsumedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (pending is null || pending.Attempts >= opt.PhoneCodeMaxAttempts)
            return Errors.Unauthorized("code_expired", "Код истёк. Запросите новый.");

        if (pending.CodeHash != Codes.Hash(phone, body.Code.Trim()))
        {
            pending.Attempts++;
            await db.SaveChangesAsync(ct);
            return Errors.Unauthorized("wrong_code", "Неверный код.");
        }

        pending.ConsumedAt = now;
        var user = await db.Users.SingleOrDefaultAsync(u => u.Phone == phone, ct);
        if (user is null)
        {
            user = new User { Id = Guid.NewGuid(), Phone = phone, CreatedAt = now };
            db.Users.Add(user);
        }
        if (phone == opt.ReviewNumber) await demo.EnsureAsync(user, ct);

        var pair = Issue(user, db, opt, tokens, now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(pair);
    }

    private static async Task<IResult> Refresh(RefreshRequest body, AiluDb db, AuthOptions opt, TokenIssuer tokens,
        TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = Codes.Hash("refresh", body.RefreshToken ?? "");
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null || existing.RevokedAt is not null || existing.ExpiresAt <= now)
            return Errors.Unauthorized("session_expired", "Сессия истекла. Войдите снова.");

        // Rotation: every refresh token works once.
        existing.RevokedAt = now;
        var user = await db.Users.SingleAsync(u => u.Id == existing.UserId, ct);
        var pair = Issue(user, db, opt, tokens, now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(pair);
    }

    private static TokenPair Issue(User user, AiluDb db, AuthOptions opt, TokenIssuer tokens, DateTimeOffset now)
    {
        var (access, expires) = tokens.Access(user.Id);
        var refresh = Codes.OpaqueToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Codes.Hash("refresh", refresh),
            ExpiresAt = now.AddDays(opt.RefreshTokenDays)
        });
        return new TokenPair(access, expires, refresh, ToDto(user));
    }

    private static async Task<IResult> GetMe(HttpContext http, AiluDb db, CancellationToken ct)
    {
        var id = CurrentUser.Id(http.User);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? Results.NotFound() : Results.Ok(ToDto(user));
    }

    // Store rules (Apple 5.1.1(v), Google Play) want deletion inside the app. AccountDeletion says what goes.
    private static async Task<IResult> DeleteMe(HttpContext http, AccountDeletion deletion, CancellationToken ct)
    {
        await deletion.DeleteAsync(CurrentUser.Id(http.User), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> PatchMe(UpdateMe body, HttpContext http, AiluDb db, CancellationToken ct)
    {
        var id = CurrentUser.Id(http.User);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return Results.NotFound();

        if (body.Name is not null)
        {
            var name = body.Name.Trim();
            if (name.Length is 0 or > 60) return Errors.Validation("name", "Имя от 1 до 60 символов.");
            user.Name = name;
        }
        if (body.Locale is not null)
        {
            if (!Locales.Contains(body.Locale)) return Errors.Validation("locale", "Язык: kk, ru или en.");
            user.Locale = body.Locale;
        }
        if (body.TimeZone is not null)
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(body.TimeZone, out _))
                return Errors.Validation("timeZone", "Неизвестный часовой пояс.");
            user.TimeZone = body.TimeZone;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(user));
    }
}
