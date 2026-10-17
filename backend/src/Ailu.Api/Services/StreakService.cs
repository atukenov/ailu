using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ailu.Api.Services;

public sealed record StreakRestoreDto(int Days, int MissedDays, int Cost, int RestoresLeft, int RestoresPerMonth, bool CanAfford);
public sealed record StreakDto(int Days, DateOnly? LastDay, StreakRestoreDto? Restore);

// Streak restores: the couple spends нұр to bring back a streak a missed day broke. Each paid
// missed day uses up one of the plan's restores for the month and costs Nur.StreakRestoreCost.
public sealed class StreakService(AiluDb db, TimeProvider clock)
{
    public static StreakState StateOf(Couple c) =>
        new(c.StreakDays, c.StreakLastDay, c.StreakForgivenOn, c.StreakLostDays, c.StreakLostLastDay, c.StreakRestartedOn);

    public static void Apply(Couple c, StreakState s)
    {
        c.StreakDays = s.Days;
        c.StreakLastDay = s.LastDay;
        c.StreakForgivenOn = s.ForgivenOn;
        c.StreakLostDays = s.LostDays;
        c.StreakLostLastDay = s.LostLastDay;
        c.StreakRestartedOn = s.RestartedOn;
    }

    public async Task<StreakDto> DtoAsync(Couple c, DateOnly today, CancellationToken ct = default)
    {
        var s = StateOf(c);
        var offer = Streak.Offer(s, today);
        var perMonth = Plans.StreakRestoresPerMonth(c.Plan);

        // An offer the plan could never pay for, even at the start of a month, is not shown.
        StreakRestoreDto? restore = null;
        if (offer is not null && offer.PaidDays.Count <= perMonth)
        {
            var left = perMonth - await UsedThisMonthAsync(c, today, ct);
            var cost = offer.PaidDays.Count * Nur.StreakRestoreCost;
            restore = new StreakRestoreDto(offer.Days, offer.PaidDays.Count, cost, Math.Max(left, 0), perMonth,
                c.NurBalance >= cost);
        }

        return new StreakDto(Streak.Current(s, today), c.StreakLastDay, restore);
    }

    public async Task<IResult> RestoreAsync(Couple c, CancellationToken ct = default)
    {
        var today = CoupleCalendar.Today(c, clock);
        var s = StateOf(c);
        var offer = Streak.Offer(s, today);
        if (offer is null) return Errors.Conflict("nothing_to_restore", "Серия не прерывалась, восстанавливать нечего.");

        var paid = offer.PaidDays.Count;
        if (paid > Plans.StreakRestoresPerMonth(c.Plan) - await UsedThisMonthAsync(c, today, ct))
            return Errors.Conflict("no_restores_left", "Восстановления на этот месяц закончились.");

        var cost = paid * Nur.StreakRestoreCost;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Check and spend in one statement, so an award saved at the same moment can't be lost
        // and the balance can't go below zero.
        var spent = await db.Couples.Where(x => x.Id == c.Id && x.NurBalance >= cost)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.NurBalance, x => x.NurBalance - cost), ct);
        if (spent == 0) return Errors.Conflict("not_enough_nur", $"Для восстановления нужно {cost} нұр.");

        // One row per missed day: the idempotency key makes a second tap, or both partners
        // tapping at once, fail instead of paying twice.
        foreach (var day in offer.PaidDays)
            db.NurLedger.Add(new NurLedgerEntry
            {
                CoupleId = c.Id,
                SourceType = NurSource.StreakRestore,
                SourceId = day.ToString("yyyy-MM-dd"),
                Amount = -Nur.StreakRestoreCost,
                IdempotencyKey = NurLedgerService.KeyFor(c.Id, NurSource.StreakRestore, day.ToString("yyyy-MM-dd"), day),
                CreatedAt = clock.GetUtcNow()
            });
        Apply(c, Streak.Restore(s, today, offer));

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Errors.Conflict("already_restored", "Серию уже восстановили.");
        }

        await db.Entry(c).ReloadAsync(ct);
        return Results.Ok(await DtoAsync(c, today, ct));
    }

    // Restores are counted per calendar month in the couple's time zone.
    private async Task<int> UsedThisMonthAsync(Couple c, DateOnly today, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-32);
        var times = await db.NurLedger
            .Where(e => e.CoupleId == c.Id && e.SourceType == NurSource.StreakRestore && e.CreatedAt >= since)
            .Select(e => e.CreatedAt).ToListAsync(ct);
        return times.Count(t => CoupleCalendar.DayOf(c, t) is var d && d.Year == today.Year && d.Month == today.Month);
    }
}
