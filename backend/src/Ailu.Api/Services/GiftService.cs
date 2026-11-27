using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ailu.Api.Services;

public enum RedeemResult { Applied, NotFound, Used, Ended }

public sealed record Redeemed(RedeemResult Result, PlanCode? Code = null, Subscription? Subscription = null);

// Gift subscriptions and promo codes, both made by the team (/v1/admin/...). Gifts are sold person to person,
// outside the app: the buyer pays the team, the team makes a gift code and hands it over. Both end up as a
// code a couple enters; entering it adds a
// Subscription row, so the plan, its expiry and the couple's limits work exactly as for a store purchase.
// A code never touches the 7-day trial: the trial is its own row and stays available (or used) as before.
public sealed class GiftService(AiluDb db, BillingService billing, TimeProvider clock)
{
    public async Task<Redeemed> RedeemAsync(CoupleScope scope, string typed, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var normalized = PlanCodes.Normalize(typed);
        var code = await db.PlanCodes.SingleOrDefaultAsync(c => c.Code == normalized, ct);
        if (code is null)
        {
            var gift = PlanCodes.AsGiftCode(normalized);
            if (gift != normalized) code = await db.PlanCodes.SingleOrDefaultAsync(c => c.Code == gift && c.Kind == PlanCodeKind.Gift, ct);
        }
        if (code is null || code.DisabledAt is not null) return new(RedeemResult.NotFound);

        // Once per person and once per couple: neither partner can have entered it before, in any couple.
        var people = scope.Couple.Members.Select(m => m.UserId).ToList();
        if (await db.CodeRedemptions.AnyAsync(r => r.CodeId == code.Id && (people.Contains(r.UserId) || r.CoupleId == scope.Couple.Id), ct))
            return new(RedeemResult.Used, code);
        if (code.Redemptions >= code.MaxRedemptions)
            return new(code.Kind == PlanCodeKind.Gift ? RedeemResult.Used : RedeemResult.Ended, code);
        if (code.ValidUntil <= now) return new(RedeemResult.Ended, code);

        // A second gift of the same plan follows the first instead of running alongside it. A store
        // subscription runs on its own: the store keeps billing until the couple cancels there.
        var provider = code.Kind == PlanCodeKind.Gift ? StoreProvider.Gift : StoreProvider.Promo;
        var start = await db.Subscriptions
            .Where(s => s.CoupleId == scope.Couple.Id && (s.Provider == StoreProvider.Gift || s.Provider == StoreProvider.Promo)
                && s.Tier == code.Tier && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Grace) && s.PeriodEnd > now)
            .MaxAsync(s => (DateTimeOffset?)s.PeriodEnd, ct) ?? now;

        var redemptionId = Guid.NewGuid();
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(), CoupleId = scope.Couple.Id, PayerUserId = scope.UserId, Provider = provider,
            StoreId = redemptionId.ToString(), ProductId = $"{code.Kind.ToString().ToLowerInvariant()}:{code.Code}", Tier = code.Tier,
            Status = SubscriptionStatus.Active, PeriodEnd = code.PeriodEnd(start), AutoRenew = false, CreatedAt = now, UpdatedAt = now
        };

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // The last use of a code goes to whoever gets here first.
            var taken = await db.PlanCodes
                .Where(c => c.Id == code.Id && (c.MaxRedemptions == null || c.Redemptions < c.MaxRedemptions))
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.Redemptions, c => c.Redemptions + 1), ct);
            if (taken == 0) return new(code.Kind == PlanCodeKind.Gift ? RedeemResult.Used : RedeemResult.Ended, code);

            db.Subscriptions.Add(subscription);
            db.CodeRedemptions.Add(new CodeRedemption
            {
                Id = redemptionId, CodeId = code.Id, UserId = scope.UserId, CoupleId = scope.Couple.Id, SubscriptionId = subscription.Id,
                RedeemedAt = now
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Both partners entered it at the same moment: one of them got it.
                db.ChangeTracker.Clear();
                return new(RedeemResult.Used, code);
            }
            await tx.CommitAsync(ct);
        }

        await billing.RecomputeAsync(scope.Couple.Id, ct);
        return new(RedeemResult.Applied, code, subscription);
    }

    public async Task<PlanCode> CreatePromoAsync(string normalized, CouplePlan tier, int months, int days, int? maxRedemptions,
        DateTimeOffset? validUntil, string? note, CancellationToken ct)
    {
        var code = new PlanCode
        {
            Id = Guid.NewGuid(), Code = normalized, Kind = PlanCodeKind.Promo, Tier = tier, Months = months, Days = days,
            MaxRedemptions = maxRedemptions, ValidUntil = validUntil, Note = note, CreatedAt = clock.GetUtcNow()
        };
        db.PlanCodes.Add(code);
        await db.SaveChangesAsync(ct);
        return code;
    }

    // A one-time code for a gift the team has been paid for. Twelve random characters, shown in groups of four.
    public async Task<PlanCode> CreateGiftAsync(CouplePlan tier, int months, string? note, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var code = new PlanCode
            {
                Id = Guid.NewGuid(), Code = PlanCodes.NewGiftCode(), Kind = PlanCodeKind.Gift, Tier = tier, Months = months,
                MaxRedemptions = 1, Note = note, CreatedAt = clock.GetUtcNow()
            };
            db.PlanCodes.Add(code);
            try
            {
                await db.SaveChangesAsync(ct);
                return code;
            }
            catch (DbUpdateException e) when (attempt < 3 && e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // 60 random bits make this all but impossible; try another code rather than fail.
                db.ChangeTracker.Clear();
            }
        }
    }
}
