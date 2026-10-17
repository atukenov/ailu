using System.Security.Claims;
using Ailu.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

// The couple a request acts on. It is always resolved from the caller's token, never from the URL,
// so one couple can never address another couple's data.
public sealed record CoupleScope(Couple Couple, CoupleMember Me, CoupleMember? Partner)
{
    public Guid UserId => Me.UserId;
    public MemberRole Role => Me.Role;
}

public static class CurrentUser
{
    public static Guid Id(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Authenticated user has no subject."));

    public static async Task<CoupleScope?> CoupleAsync(ClaimsPrincipal user, AiluDb db, CancellationToken ct = default)
    {
        var userId = Id(user);
        var me = await db.CoupleMembers
            .Include(m => m.Couple!).ThenInclude(c => c.Members).ThenInclude(m => m.User)
            .Where(m => m.UserId == userId && m.LeftAt == null && m.Couple!.Status != CoupleStatus.Ended)
            .SingleOrDefaultAsync(ct);
        if (me is null) return null;

        var partner = me.Couple!.Members.FirstOrDefault(m => m.UserId != userId && m.LeftAt == null);
        return new CoupleScope(me.Couple, me, partner);
    }
}

public static class CoupleCalendar
{
    // Kazakhstan has been on a single UTC+5 zone since March 2024; used if the zone id is unknown.
    private static readonly TimeSpan Fallback = TimeSpan.FromHours(5);

    public static DateOnly Today(Couple couple, TimeProvider clock) => DayOf(couple, clock.GetUtcNow());

    public static DateOnly DayOf(Couple couple, DateTimeOffset instant)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(couple.TimeZone);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, tz).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(instant.ToOffset(Fallback).DateTime);
        }
    }
}
