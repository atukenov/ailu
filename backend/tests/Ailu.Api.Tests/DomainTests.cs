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
