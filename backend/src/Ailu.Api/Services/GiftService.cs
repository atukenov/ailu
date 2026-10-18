using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ailu.Api.Services;

public enum RedeemResult { Applied, NotFound, Used, Ended }

public sealed record Redeemed(RedeemResult Result, PlanCode? Code = null, Subscription? Subscription = null);

// Gift subscriptions and promo codes. Both end up as a code a couple enters; entering it adds a
// Subscription row, so the plan, its expiry and the couple's limits work exactly as for a store purchase.
// A code never touches the 7-day trial: the trial is its own row and stays available (or used) as before.
public sealed class GiftService(AiluDb db, BillingService billing, IGiftCheckout checkout, BillingOptions options, TimeProvider clock,
    ILogger<GiftService> log)
{
    public bool Available => checkout.Available;

    public GiftProduct? Product(string id) => options.Gifts.FirstOrDefault(p => p.Id == id);

    // A gift is ordered, then paid on the provider's page. The code exists only once the provider says paid.
    public async Task<GiftOrder> OrderAsync(Guid buyer, GiftProduct product, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var order = new GiftOrder
        {
            Id = Guid.NewGuid(), BuyerUserId = buyer, ProductId = product.Id, Tier = product.Tier, Months = product.Months,
            PriceKzt = product.PriceKzt, Status = GiftOrderStatus.Pending, Provider = checkout.Provider, CreatedAt = now, UpdatedAt = now
        };
        var session = await checkout.StartAsync(order, ct);
        order.CheckoutId = session.CheckoutId;
        order.PaymentUrl = session.PaymentUrl;
        db.GiftOrders.Add(order);
        await db.SaveChangesAsync(ct);
        // A checkout with nothing to open may already be paid.
        if (session.PaymentUrl is null) await RefreshAsync(order, ct);
        return order;
    }

    public async Task RefreshAsync(string checkoutId, CancellationToken ct)
    {
        var order = await db.GiftOrders.SingleOrDefaultAsync(o => o.Provider == checkout.Provider && o.CheckoutId == checkoutId, ct);
        if (order is not null) await RefreshAsync(order, ct);
    }

    // Asks the provider where the payment stands and follows it: paid gets a code, a refund withdraws it.
    public async Task RefreshAsync(GiftOrder order, CancellationToken ct)
    {
        if (order.CheckoutId is null || order.Provider != checkout.Provider) return;
        if (order.Status is GiftOrderStatus.Cancelled or GiftOrderStatus.Refunded) return;
        var status = await checkout.FetchAsync(order.CheckoutId, ct);
        if (status is null) return;
        var now = clock.GetUtcNow();

        switch (status.Value)
        {
            case GiftPaymentStatus.Paid when order.Status == GiftOrderStatus.Pending:
                await IssueAsync(order, now, ct);
                break;
            case GiftPaymentStatus.Cancelled when order.Status == GiftOrderStatus.Pending:
                order.Status = GiftOrderStatus.Cancelled;
                order.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                break;
            case GiftPaymentStatus.Refunded when order.Status == GiftOrderStatus.Paid:
                await RefundAsync(order, now, ct);
                break;
        }
    }

    private async Task IssueAsync(GiftOrder order, DateTimeOffset now, CancellationToken ct)
    {
        var code = new PlanCode
        {
            Id = Guid.NewGuid(), Code = PlanCodes.NewGiftCode(), Kind = PlanCodeKind.Gift, Tier = order.Tier, Months = order.Months,
            MaxRedemptions = 1, GiftOrderId = order.Id, CreatedAt = now
        };
        db.PlanCodes.Add(code);
        order.Status = GiftOrderStatus.Paid;
        order.PaidAt = now;
        order.CodeId = code.Id;
        order.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The webhook and the buyer's screen asked at once: the first one issued the code.
            db.ChangeTracker.Clear();
        }
    }

    // A refunded gift stops working. If it was already entered, the plan it gave ends now.
    private async Task RefundAsync(GiftOrder order, DateTimeOffset now, CancellationToken ct)
    {
        order.Status = GiftOrderStatus.Refunded;
        order.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        if (order.CodeId is not { } codeId) return;

        await db.PlanCodes.Where(c => c.Id == codeId && c.DisabledAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.DisabledAt, now), ct);
        var subscriptions = db.CodeRedemptions.Where(r => r.CodeId == codeId).Select(r => r.SubscriptionId);
        var couples = await db.Subscriptions.Where(s => subscriptions.Contains(s.Id)).Select(s => s.CoupleId).ToListAsync(ct);
        await db.Subscriptions.Where(s => subscriptions.Contains(s.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SubscriptionStatus.Revoked).SetProperty(s => s.UpdatedAt, now), ct);
        foreach (var id in couples) await billing.RecomputeAsync(id, ct);
        log.LogInformation("Gift order {OrderId} refunded", order.Id);
    }

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
}
