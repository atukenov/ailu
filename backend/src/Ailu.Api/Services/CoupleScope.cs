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

    public static DateOnly DayOf(Couple couple, DateTimeOffset instant) => DateOnly.FromDateTime(Local(couple, instant).DateTime);

    public static TimeOnly TimeOf(Couple couple, DateTimeOffset instant) => TimeOnly.FromDateTime(Local(couple, instant).DateTime);

    // The instant the couple's calendar day starts, e.g. so a widget can turn over at their midnight.
    public static DateTimeOffset StartOf(Couple couple, DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(couple.TimeZone);
            return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
        }
        catch (TimeZoneNotFoundException)
        {
            return new DateTimeOffset(midnight, Fallback);
        }
    }

    private static DateTimeOffset Local(Couple couple, DateTimeOffset instant)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(couple.TimeZone));
        }
        catch (TimeZoneNotFoundException)
        {
            return instant.ToOffset(Fallback);
        }
    }
}
