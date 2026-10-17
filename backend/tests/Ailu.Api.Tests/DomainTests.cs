using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;

namespace Ailu.Api.Tests;

public class PhaseTests
{
    [Theory]
    [InlineData(0, 1, "new-moon")]
    [InlineData(499, 1, "new-moon")]
    [InlineData(500, 1, "waxing-crescent")]
    [InlineData(4_500, 1, "full-moon")]
    [InlineData(7_000, 1, "supermoon")]
    [InlineData(9_999, 1, "supermoon")]
    [InlineData(10_000, 2, "new-moon")]
    [InlineData(11_600, 2, "first-quarter")]
    public void Phase_follows_earned_nur(int earned, int cycle, string slug)
    {
        var p = Phases.For(earned);
        Assert.Equal(cycle, p.Cycle);
        Assert.Equal(slug, p.Phase.Slug);
    }

    [Fact]
    public void Fill_is_share_of_current_phase()
    {
        var p = Phases.For(1_000);
        Assert.Equal(500, p.PhaseStart);
        Assert.Equal(1_500, p.NextAt);
        Assert.Equal(0.5, p.Fill);
        Assert.Equal("Свой ритм", p.Next!.Name);
    }

    [Fact]
    public void Supermoon_points_to_next_cycle()
    {
        var p = Phases.For(7_000);
        Assert.Equal(10_000, p.NextAt);
        Assert.Equal("new-moon", p.Next!.Slug);
    }
}

public class StreakTests
{
    private static readonly DateOnly D0 = new(2026, 10, 1);
    private static StreakState Run(params int[] days) =>
        days.Aggregate(default(StreakState), (s, d) => Streak.CountDay(s, D0.AddDays(d)));

    [Fact]
    public void Consecutive_days_grow_the_streak() => Assert.Equal(3, Run(0, 1, 2).Days);

    [Fact]
    public void Same_day_twice_counts_once() => Assert.Equal(2, Run(0, 1, 1).Days);

    [Fact]
    public void One_missed_night_is_forgiven()
    {
        var s = Run(0, 1, 3);
        Assert.Equal(3, s.Days);
        Assert.Equal(D0.AddDays(2), s.ForgivenOn);
    }

    [Fact]
    public void Second_miss_within_a_week_restarts()
    {
        Assert.Equal(1, Run(0, 2, 4).Days);
    }

    [Fact]
    public void Misses_a_week_apart_are_both_forgiven()
    {
        // Missed day 1 and day 8.
        Assert.Equal(9, Run(0, 2, 3, 4, 5, 6, 7, 9, 10).Days);
    }

    [Fact]
    public void Two_missed_nights_restart() => Assert.Equal(1, Run(0, 1, 4).Days);

    [Fact]
    public void Current_shows_streak_while_it_can_still_continue()
    {
        var s = Run(0, 1, 2);
        Assert.Equal(3, Streak.Current(s, D0.AddDays(2)));
        Assert.Equal(3, Streak.Current(s, D0.AddDays(3)));
        Assert.Equal(3, Streak.Current(s, D0.AddDays(4))); // one night can still be forgiven
        Assert.Equal(0, Streak.Current(s, D0.AddDays(5)));
    }

    [Fact]
    public void Live_streak_has_nothing_to_restore() => Assert.Null(Streak.Offer(Run(0, 1, 2), D0.AddDays(4)));

    [Fact]
    public void Lapsed_streak_pays_for_missed_days_the_free_night_does_not_cover()
    {
        var s = Run(0, 1, 2);
        var offer = Streak.Offer(s, D0.AddDays(6))!; // missed days 3, 4 and 5
        Assert.Equal(3, offer.Days);
        Assert.Equal(D0.AddDays(3), offer.Forgiven);
        Assert.Equal([D0.AddDays(4), D0.AddDays(5)], offer.PaidDays);

        var restored = Streak.Restore(s, D0.AddDays(6), offer);
        Assert.Equal(3, Streak.Current(restored, D0.AddDays(6)));
        Assert.Equal(4, Streak.CountDay(restored, D0.AddDays(6)).Days);
    }

    [Fact]
    public void Streak_lost_on_starting_over_can_be_restored_for_a_week()
    {
        var s = Run(0, 1, 2, 3, 4, 7, 8); // missed 5 and 6: restarted on day 7
        Assert.Equal(2, s.Days);
        var offer = Streak.Offer(s, D0.AddDays(8))!;
        Assert.Equal(5, offer.Days);
        Assert.Equal([D0.AddDays(6)], offer.PaidDays);

        var restored = Streak.Restore(s, D0.AddDays(8), offer);
        Assert.Equal(7, restored.Days);
        Assert.Null(Streak.Offer(restored, D0.AddDays(8)));
        Assert.Equal(8, Streak.CountDay(restored, D0.AddDays(9)).Days);

        Assert.NotNull(Streak.Offer(Run(0, 1, 2, 3, 4, 7, 8, 9, 10, 11, 12, 13), D0.AddDays(13)));
        Assert.Null(Streak.Offer(Run(0, 1, 2, 3, 4, 7, 8, 9, 10, 11, 12, 13, 14), D0.AddDays(14)));
    }

    [Fact]
    public void Night_forgiven_this_week_is_paid_for()
    {
        var s = Run(0, 2, 3, 5); // forgave day 1, then missing day 4 restarted the streak
        Assert.Equal(1, s.Days);
        Assert.Equal([D0.AddDays(4)], Streak.Offer(s, D0.AddDays(5))!.PaidDays);
    }
}

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+7 701 234 56 78", "+77012345678")]
    [InlineData("87012345678", "+77012345678")]
    [InlineData("7012345678", "+77012345678")]
    [InlineData("8 (747) 123-45-67", "+77471234567")]
    public void Normalizes_kz_mobile(string input, string expected) => Assert.Equal(expected, PhoneNumber.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("+1 212 555 0100")]
    [InlineData("+7 495 123 45 67")] // Moscow landline: +7 but not a KZ mobile
    [InlineData("12345")]
    public void Rejects_other_numbers(string input) => Assert.Null(PhoneNumber.Normalize(input));
}

public class DailyPickTests
{
    [Fact]
    public void Picks_unused_items_before_repeating()
    {
        var library = new[] { 1, 2, 3 };
        var couple = Guid.NewGuid();
        var used = new List<int>();
        for (var i = 0; i < 3; i++) used.Add(DailyContentService.PickLeastUsed(library, used, couple, new DateOnly(2026, 1, 1).AddDays(i)));
        Assert.Equal([1, 2, 3], used.Order());
    }
}
