namespace Ailu.Api.Domain;

// Lost* remember the streak that broke most recently, so the couple can still restore it after
// the new streak has started. RestartedOn is the first day of the new streak.
public readonly record struct StreakState(int Days, DateOnly? LastDay, DateOnly? ForgivenOn,
    int LostDays = 0, DateOnly? LostLastDay = null, DateOnly? RestartedOn = null);

// What a restore would bring back: the streak's length, and the missed days the couple pays for.
// A missed night the free weekly forgiveness can still cover is not paid for.
public sealed record RestoreOffer(int Days, IReadOnlyList<DateOnly> PaidDays, DateOnly? Forgiven);

// A streak day is a day on which both partners did something shared. One missed night a week
// is forgiven for free, so a single gap does not break the streak.
public static class Streak
{
    public const int ForgivenessWindowDays = 7;

    // A broken streak can be restored until a week after the new one started.
    public const int RestoreWindowDays = 7;

    // Called once, when the second partner's activity for `day` is recorded.
    public static StreakState CountDay(StreakState s, DateOnly day)
    {
        if (s.LastDay is not DateOnly last) return s with { Days = 1, LastDay = day };
        if (day <= last) return s;

        var gap = day.DayNumber - last.DayNumber;
        if (gap == 1) return s with { Days = s.Days + 1, LastDay = day };

        var missed = last.AddDays(1);
        if (gap == 2 && CanForgive(s, missed)) return s with { Days = s.Days + 1, LastDay = day, ForgivenOn = missed };

        return new StreakState(1, day, s.ForgivenOn, s.Days, last, day);
    }

    // What to show on `today`: the streak is still alive if it can still be continued today.
    public static int Current(StreakState s, DateOnly today)
    {
        if (s.LastDay is not DateOnly last) return 0;
        if (today <= last.AddDays(1)) return s.Days;
        if (today == last.AddDays(2) && CanForgive(s, last.AddDays(1))) return s.Days;
        return 0;
    }

    // A restore is offered either for the streak that has just lapsed (no one has started over yet),
    // or for the one that broke when the couple started over in the last week.
    public static RestoreOffer? Offer(StreakState s, DateOnly today)
    {
        if (s.LastDay is not DateOnly last) return null;

        if (Current(s, today) == 0)
            return s.Days > 0 ? Price(s, last.AddDays(1), today, s.Days) : null;

        if (s.LostLastDay is DateOnly lost && s.RestartedOn is DateOnly restarted && s.LostDays > 0 &&
            today.DayNumber - restarted.DayNumber < RestoreWindowDays)
            return Price(s, lost.AddDays(1), restarted, s.LostDays);

        return null;
    }

    public static StreakState Restore(StreakState s, DateOnly today, RestoreOffer offer)
    {
        var forgivenOn = Latest(s.ForgivenOn, offer.Forgiven);

        // The lapsed streak carries on as if the couple were here yesterday; today still has to count.
        if (Current(s, today) == 0)
            return s with { LastDay = today.AddDays(-1), ForgivenOn = forgivenOn };

        return new StreakState(s.LostDays + s.Days, s.LastDay, forgivenOn);
    }

    // Missed days run from `from` up to, not including, `until`.
    private static RestoreOffer Price(StreakState s, DateOnly from, DateOnly until, int days)
    {
        var missed = Enumerable.Range(0, until.DayNumber - from.DayNumber).Select(from.AddDays).ToList();
        DateOnly? forgiven = missed.Any(d => CanForgive(s, d)) ? missed.First(d => CanForgive(s, d)) : null;
        return new RestoreOffer(days, missed.Where(d => d != forgiven).ToList(), forgiven);
    }

    private static DateOnly? Latest(DateOnly? a, DateOnly? b) => a is null ? b : b is null ? a : a > b ? a : b;

    private static bool CanForgive(StreakState s, DateOnly missed) =>
        s.ForgivenOn is not DateOnly f || missed.DayNumber - f.DayNumber >= ForgivenessWindowDays;
}
