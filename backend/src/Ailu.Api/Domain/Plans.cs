using Ailu.Api.Data;

namespace Ailu.Api.Domain;

// Free couples keep up to 200 photos (Lev, 2026-10-17). What the paid plans get is decided later;
// until then they have no photo count limit, only the byte quotas from the roadmap (document 7).
// The byte quota also backs up the free plan: 200 processed photos are about 250 MB.
public static class Plans
{
    public const int FreePhotos = 200;

    // Free couples work on two goals at a time; finished and removed goals don't count (gamification
    // and monetization docs: "Free 2 цели, Plus и выше без лимита").
    public const int FreeGoals = 2;

    public static int? GoalLimit(CouplePlan plan) => plan == CouplePlan.Free ? FreeGoals : null;

    // The task of the day, plus two more on request for paid couples (monetization doc: Free 1, Plus 3
    // a day; Premium has "всё из Plus", its personal tasks come later).
    public static int TasksPerDay(CouplePlan plan) => plan == CouplePlan.Free ? 1 : 3;

    public static int? PhotoLimit(CouplePlan plan) => plan == CouplePlan.Free ? FreePhotos : null;

    // Streak restores a month, from the gamification design (document 3).
    public static int StreakRestoresPerMonth(CouplePlan plan) => plan switch
    {
        CouplePlan.Plus => 3,
        CouplePlan.Premium => 5,
        _ => 1
    };

    public static long QuotaBytes(CouplePlan plan) => plan switch
    {
        CouplePlan.Plus => 10_000_000_000,
        CouplePlan.Premium => 30_000_000_000,
        _ => 1_000_000_000
    };
}
