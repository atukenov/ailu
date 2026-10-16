using Ailu.Api.Data;
using Ailu.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

public sealed class ActivityService(AiluDb db, TimeProvider clock)
{
    // Marks that `userId` did something shared on `day`. When this makes both partners active
    // for the day, the day counts toward the streak.
    public async Task RecordAsync(Couple couple, Guid userId, DateOnly day, CancellationToken ct = default)
    {
        var seen = db.CoupleActivities.Local.Any(a => a.CoupleId == couple.Id && a.Date == day && a.UserId == userId) ||
                   await db.CoupleActivities.AnyAsync(a => a.CoupleId == couple.Id && a.Date == day && a.UserId == userId, ct);
        if (seen) return;

        db.CoupleActivities.Add(new CoupleActivity
        {
            CoupleId = couple.Id, Date = day, UserId = userId, CreatedAt = clock.GetUtcNow()
        });

        var partnerActive = await db.CoupleActivities.AnyAsync(a => a.CoupleId == couple.Id && a.Date == day && a.UserId != userId, ct);
        if (!partnerActive) return;

        var next = Streak.CountDay(new StreakState(couple.StreakDays, couple.StreakLastDay, couple.StreakForgivenOn), day);
        couple.StreakDays = next.Days;
        couple.StreakLastDay = next.LastDay;
        couple.StreakForgivenOn = next.ForgivenOn;
    }
}
