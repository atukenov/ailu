using Ailu.Api.Data;

namespace Ailu.Api.Domain;

// Photos a couple keeps: Free 200, Plus 500, Premium 1 200 (Lev, 2026-10-17; no memory packs on top).
// The byte quotas only back the count up: a processed photo is about 1.25 MB, so each quota leaves
// room for photos four times that size.
public static class Plans
{
    public const int FreePhotos = 200;
    public const int PlusPhotos = 500;
    public const int PremiumPhotos = 1200;

    // Free couples work on two goals at a time; finished and removed goals don't count (gamification
    // and monetization docs: "Free 2 цели, Plus и выше без лимита").
    public const int FreeGoals = 2;

    public static int? GoalLimit(CouplePlan plan) => plan == CouplePlan.Free ? FreeGoals : null;

    // The task of the day, plus two more on request for paid couples (monetization doc: Free 1, Plus 3
    // a day; Premium has "всё из Plus", its personal tasks come later).
    public static int TasksPerDay(CouplePlan plan) => plan == CouplePlan.Free ? 1 : 3;

    public static int PhotoLimit(CouplePlan plan) => plan switch
    {
        CouplePlan.Plus => PlusPhotos,
        CouplePlan.Premium => PremiumPhotos,
        _ => FreePhotos
    };

    // Streak restores a month, from the gamification design (document 3).
    public static int StreakRestoresPerMonth(CouplePlan plan) => plan switch
    {
        CouplePlan.Plus => 3,
        CouplePlan.Premium => 5,
        _ => 1
    };

    public static long QuotaBytes(CouplePlan plan) => plan switch
    {
        CouplePlan.Plus => 2_500_000_000,
        CouplePlan.Premium => 6_000_000_000,
        _ => 1_000_000_000
    };
}
