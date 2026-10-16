namespace Ailu.Api.Domain;

public readonly record struct StreakState(int Days, DateOnly? LastDay, DateOnly? ForgivenOn);

// A streak day is a day on which both partners did something shared. One missed night a week
// is forgiven for free, so a single gap does not break the streak.
public static class Streak
{
    public const int ForgivenessWindowDays = 7;

    // Called once, when the second partner's activity for `day` is recorded.
    public static StreakState CountDay(StreakState s, DateOnly day)
    {
        if (s.LastDay is not DateOnly last) return new StreakState(1, day, s.ForgivenOn);
        if (day <= last) return s;

        var gap = day.DayNumber - last.DayNumber;
        if (gap == 1) return s with { Days = s.Days + 1, LastDay = day };

        var missed = last.AddDays(1);
        if (gap == 2 && CanForgive(s, missed)) return new StreakState(s.Days + 1, day, missed);

        return new StreakState(1, day, s.ForgivenOn);
    }

    // What to show on `today`: the streak is still alive if it can still be continued today.
    public static int Current(StreakState s, DateOnly today)
    {
        if (s.LastDay is not DateOnly last) return 0;
        if (today <= last.AddDays(1)) return s.Days;
        if (today == last.AddDays(2) && CanForgive(s, last.AddDays(1))) return s.Days;
        return 0;
    }

    private static bool CanForgive(StreakState s, DateOnly missed) =>
        s.ForgivenOn is not DateOnly f || missed.DayNumber - f.DayNumber >= ForgivenessWindowDays;
}
