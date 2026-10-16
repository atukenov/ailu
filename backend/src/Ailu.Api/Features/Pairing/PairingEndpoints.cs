using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ailu.Api.Features.Pairing;

public sealed record InviteRequest(string Phone);
public sealed record InvitationDto(Guid Id, string Phone, DateTimeOffset ExpiresAt);
public sealed record PartnerDto(Guid Id, string? Name);
public sealed record CoupleDto(Guid Id, DateTimeOffset StartedAt, string Status, int DaysTogether, PartnerDto? Partner);
public sealed record PhaseDto(int Cycle, int Index, string Slug, string Name, string Moon, int Earned, int Balance,
    int PhaseStart, int? NextAt, string? NextName, double Fill);

public static class PairingEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/couple").WithTags("Couple").RequireAuthorization();
        g.MapPost("/invitations", Invite);
        g.MapPost("/invitations/{code}/accept", Accept);
        g.MapGet("/", Get);
        g.MapGet("/phase", GetPhase);
        g.MapDelete("/", End);
    }

    public static PhaseDto ToPhase(Couple c)
    {
        var p = Phases.For(c.NurEarned);
        return new PhaseDto(p.Cycle, p.Phase.Index, p.Phase.Slug, p.Phase.Name, p.Phase.Moon, c.NurEarned, c.NurBalance,
            p.PhaseStart, p.NextAt, p.Next?.Name, Math.Round(p.Fill, 4));
    }

    private static async Task<IResult> Invite(InviteRequest body, HttpContext http, AiluDb db, ISmsSender sms,
        AuthOptions opt, TimeProvider clock, CancellationToken ct)
    {
        var userId = CurrentUser.Id(http.User);
        var phone = PhoneNumber.Normalize(body.Phone);
        if (phone is null) return Errors.Validation("phone", "Нужен казахстанский номер в формате +7 7XX XXX XX XX.");

        var me = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (me.Phone == phone) return Errors.Validation("phone", "Это ваш собственный номер.");
        if (await CurrentUser.CoupleAsync(http.User, db, ct) is not null)
            return Errors.Conflict("already_in_couple", "У вас уже есть пара.");

        var now = clock.GetUtcNow();
        var invitation = new Invitation
        {
            Id = Guid.NewGuid(), InviterUserId = userId, Phone = phone, Code = Codes.SixDigits(),
            CreatedAt = now, ExpiresAt = now.AddHours(opt.InvitationHours)
        };
        db.Invitations.Add(invitation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A six-digit code collided with a live one; one retry from the client is enough.
            return Errors.Conflict("retry", "Не получилось создать код. Попробуйте ещё раз.");
        }

        var who = me.Name ?? "Ваш партнёр";
        await sms.SendAsync(phone, $"{who} приглашает вас в Ailu. Код пары: {invitation.Code}", ct);
        return Results.Created($"/v1/couple/invitations/{invitation.Id}", new InvitationDto(invitation.Id, phone, invitation.ExpiresAt));
    }

    private static async Task<IResult> Accept(string code, HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct)
    {
        var userId = CurrentUser.Id(http.User);
        var now = clock.GetUtcNow();
        var me = await db.Users.SingleAsync(u => u.Id == userId, ct);

        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Code == code, ct);
        // The code only works for the number it was sent to, so a leaked code is useless to anyone else.
        if (invitation is null || invitation.Phone != me.Phone || invitation.AcceptedAt is not null || invitation.ExpiresAt <= now)
            return Errors.NotFound("invitation_not_found", "Код не подходит или истёк. Попросите партнёра отправить новый.");

        if (await CurrentUser.CoupleAsync(http.User, db, ct) is not null)
            return Errors.Conflict("already_in_couple", "У вас уже есть пара.");
        var inviterBusy = await db.CoupleMembers.AnyAsync(m => m.UserId == invitation.InviterUserId && m.LeftAt == null
            && m.Couple!.Status != CoupleStatus.Ended, ct);
        if (inviterBusy) return Errors.Conflict("inviter_in_couple", "Пригласивший уже состоит в паре.");

        var inviter = await db.Users.SingleAsync(u => u.Id == invitation.InviterUserId, ct);
        var couple = new Couple
        {
            Id = Guid.NewGuid(), StartedAt = now, Status = CoupleStatus.Active, TimeZone = inviter.TimeZone,
            Members =
            [
                new CoupleMember { UserId = inviter.Id, Role = MemberRole.A, JoinedAt = now },
                new CoupleMember { UserId = me.Id, Role = MemberRole.B, JoinedAt = now }
            ]
        };
        db.Couples.Add(couple);
        invitation.AcceptedAt = now;
        invitation.CoupleId = couple.Id;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Errors.Conflict("already_in_couple", "Один из вас уже состоит в паре.");
        }

        return Results.Ok(new CoupleDto(couple.Id, couple.StartedAt, couple.Status.ToString(), 1, new PartnerDto(inviter.Id, inviter.Name)));
    }

    private static async Task<IResult> Get(HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var c = scope.Couple;
        var startDay = DateOnly.FromDateTime(c.StartedAt.UtcDateTime);
        var days = CoupleCalendar.Today(c, clock).DayNumber - startDay.DayNumber + 1;
        var partner = scope.Partner?.User;
        return Results.Ok(new CoupleDto(c.Id, c.StartedAt, c.Status.ToString(), Math.Max(days, 1),
            partner is null ? null : new PartnerDto(partner.Id, partner.Name)));
    }

    private static async Task<IResult> GetPhase(HttpContext http, AiluDb db, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        return scope is null ? Errors.NoCouple() : Results.Ok(ToPhase(scope.Couple));
    }

    // Ending a couple is a state change, never a DELETE of rows: the history stays intact in case
    // the couple comes back, and nothing that references it breaks.
    private static async Task<IResult> End(HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();

        var now = clock.GetUtcNow();
        scope.Couple.Status = CoupleStatus.Ended;
        scope.Couple.EndedAt = now;
        foreach (var m in scope.Couple.Members.Where(m => m.LeftAt is null)) m.LeftAt = now;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
