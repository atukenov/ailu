using Ailu.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

public sealed class NurLedgerService(AiluDb db, TimeProvider clock)
{
    public static string KeyFor(Guid coupleId, NurSource source, string sourceId, DateOnly day) =>
        $"{coupleId:N}:{source}:{sourceId}:{day:yyyy-MM-dd}";

    // Adds a ledger row and updates the couple's cached totals in the same unit of work.
    // Awarding the same (couple, source, sourceId, day) twice is a no-op; the unique index on
    // IdempotencyKey backs this up if two requests race past the check.
    public async Task<bool> AwardAsync(Couple couple, NurSource source, string sourceId, int amount, DateOnly day,
        CancellationToken ct = default)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Awards are positive; spending is separate.");

        var key = KeyFor(couple.Id, source, sourceId, day);
        if (db.NurLedger.Local.Any(e => e.IdempotencyKey == key) ||
            await db.NurLedger.AnyAsync(e => e.IdempotencyKey == key, ct))
            return false;

        db.NurLedger.Add(new NurLedgerEntry
        {
            CoupleId = couple.Id,
            SourceType = source,
            SourceId = sourceId,
            Amount = amount,
            IdempotencyKey = key,
            CreatedAt = clock.GetUtcNow()
        });
        couple.NurEarned += amount;
        couple.NurBalance += amount;
        return true;
    }
}
