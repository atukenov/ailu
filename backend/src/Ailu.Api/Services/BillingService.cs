using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ailu.Api.Services;

public enum ClaimResult { Applied, NotFound, OtherOwner, UnknownProduct }

// Keeps Subscriptions in step with the stores and Couple.Plan in step with Subscriptions. One partner's
// subscription gives the couple its plan, so both partners get it.
public sealed class BillingService(AiluDb db, IEnumerable<IStoreClient> stores, BillingOptions options, TimeProvider clock,
    ILogger<BillingService> log)
{
    public IStoreClient Store(StoreProvider provider) => stores.Single(s => s.Provider == provider);

    // A partner bought or restored a purchase in the app. The store decides what it is worth; the app
    // only tells us where to look.
    public async Task<ClaimResult> ClaimAsync(CoupleScope scope, StoreProvider provider, string storeId, CancellationToken ct)
    {
        var purchase = await Store(provider).FetchAsync(storeId, ct);
        if (purchase is null) return ClaimResult.NotFound;
        if (options.TierOf(provider, purchase.ProductId) is null) return ClaimResult.UnknownProduct;

        // The app attaches the buyer's user id at purchase. A purchase made from another Ailu account on the
        // same Apple ID or Google account stays with that account.
        if (purchase.AccountId is { Length: > 0 } account && !string.Equals(account, scope.UserId.ToString(), StringComparison.OrdinalIgnoreCase))
            return ClaimResult.OtherOwner;

        var existing = await db.Subscriptions.SingleOrDefaultAsync(s => s.Provider == provider && s.StoreId == purchase.StoreId, ct);
        if (existing is not null && existing.PayerUserId != scope.UserId) return ClaimResult.OtherOwner;

        await ApplyAsync(purchase, existing, scope.UserId, scope.Couple.Id, ct);
        return ClaimResult.Applied;
    }

    // A store told us a subscription changed (renewed, cancelled, refunded). Purchases nobody has claimed
    // yet are left for the app to claim, since only then is the couple known.
    public async Task RefreshAsync(StoreProvider provider, string storeId, CancellationToken ct)
    {
        var purchase = await Store(provider).FetchAsync(storeId, ct);
        if (purchase is null) return;
        var existing = await db.Subscriptions.SingleOrDefaultAsync(s => s.Provider == provider && s.StoreId == purchase.StoreId, ct);
        if (existing is null) return;
        await ApplyAsync(purchase, existing, existing.PayerUserId, existing.CoupleId, ct);
    }

    private async Task ApplyAsync(StorePurchase purchase, Subscription? row, Guid payer, Guid coupleId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var tier = options.TierOf(purchase.Provider, purchase.ProductId);
        if (tier is null)
        {
            log.LogWarning("Ignoring {Provider} purchase of unknown product {Product}", purchase.Provider, purchase.ProductId);
            return;
        }

        var touched = new HashSet<Guid> { coupleId };
        if (row is null)
        {
            row = new Subscription
            {
                Id = Guid.NewGuid(), Provider = purchase.Provider, StoreId = purchase.StoreId, ProductId = purchase.ProductId,
                PayerUserId = payer, CoupleId = coupleId, CreatedAt = now
            };
            db.Subscriptions.Add(row);
        }
        else if (row.CoupleId != coupleId)
        {
            // The payer restored it in a new couple: after a breakup the subscription stays with whoever pays.
            touched.Add(row.CoupleId);
            row.CoupleId = coupleId;
        }

        row.ProductId = purchase.ProductId;
        row.Tier = tier.Value;
        row.Status = purchase.Status;
        row.PeriodEnd = purchase.PeriodEnd;
        row.AutoRenew = purchase.AutoRenew;
        row.Sandbox = purchase.Sandbox;
        row.UpdatedAt = now;

        // Google issues a new token on upgrade, downgrade or resubscribe; the old one stops counting.
        if (purchase.ReplacesStoreId is { } old)
        {
            var replaced = await db.Subscriptions.SingleOrDefaultAsync(s => s.Provider == purchase.Provider && s.StoreId == old, ct);
            if (replaced is not null)
            {
                replaced.Status = SubscriptionStatus.Replaced;
                replaced.UpdatedAt = now;
                touched.Add(replaced.CoupleId);
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same purchase claimed twice at once (the app retries, the webhook lands): the first one won.
            db.ChangeTracker.Clear();
            return;
        }

        if (purchase.NeedsAcknowledge) await Store(purchase.Provider).AcknowledgeAsync(purchase, ct);
        foreach (var id in touched) await RecomputeAsync(id, ct);
    }

    // The 7-day Premium trial: once per couple, without a card (monetization doc). It counts as used if
    // either partner has had one in any couple, so splitting up and pairing again doesn't renew it.
    public async Task<bool> TrialAvailableAsync(CoupleScope scope, CancellationToken ct)
    {
        var people = scope.Couple.Members.Select(m => m.UserId).ToList();
        var couples = db.CoupleMembers.Where(m => people.Contains(m.UserId)).Select(m => m.CoupleId);
        return !await db.Subscriptions.AnyAsync(s => s.Provider == StoreProvider.Trial && couples.Contains(s.CoupleId), ct);
    }

    public async Task<bool> StartTrialAsync(CoupleScope scope, CancellationToken ct)
    {
        if (!await TrialAvailableAsync(scope, ct)) return false;
        var now = clock.GetUtcNow();
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(), CoupleId = scope.Couple.Id, PayerUserId = scope.UserId, Provider = StoreProvider.Trial,
            StoreId = scope.Couple.Id.ToString(), ProductId = "trial", Tier = CouplePlan.Premium, Status = SubscriptionStatus.Active,
            PeriodEnd = now.AddDays(BillingOptions.TrialDays), CreatedAt = now, UpdatedAt = now
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
        await RecomputeAsync(scope.Couple.Id, ct);
        return true;
    }

    // A new couple: whatever either partner still pays for comes along, gifts and promo codes they entered too.
    public async Task AttachPayersAsync(Couple couple, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var people = couple.Members.Select(m => m.UserId).ToList();
        var moved = await db.Subscriptions
            .Where(s => people.Contains(s.PayerUserId) && s.Provider != StoreProvider.Trial && s.CoupleId != couple.Id
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Grace) && s.PeriodEnd > now)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.CoupleId, couple.Id).SetProperty(s => s.UpdatedAt, now), ct);
        if (moved > 0) await RecomputeAsync(couple.Id, ct);
    }

    // Couple.Plan becomes the best plan any live subscription of the couple pays for.
    public async Task<CouplePlan> RecomputeAsync(Guid coupleId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var tiers = await db.Subscriptions
            .Where(s => s.CoupleId == coupleId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Grace) && s.PeriodEnd > now)
            .Select(s => s.Tier).ToListAsync(ct);
        var plan = tiers.Count == 0 ? CouplePlan.Free : tiers.Max();
        await db.Couples.Where(c => c.Id == coupleId && c.Plan != plan).ExecuteUpdateAsync(u => u.SetProperty(c => c.Plan, plan), ct);
        return plan;
    }

    // The safety net for lost webhooks: subscriptions past their end are asked about once more (the store may
    // have renewed them), and couples whose last subscription ran out go back to Free. Nothing the couple
    // made is touched; above the free limits it simply can't grow (monetization doc).
    public async Task<int> RunExpiryAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var due = await db.Subscriptions
            .Where(s => (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Grace) && s.PeriodEnd <= now)
            .OrderBy(s => s.PeriodEnd).Take(100).ToListAsync(ct);

        foreach (var s in due)
        {
            // Only a store can renew; a trial or a code simply ends.
            if (s.Provider is StoreProvider.Apple or StoreProvider.Google)
            {
                try
                {
                    await RefreshAsync(s.Provider, s.StoreId, ct);
                }
                catch (StoreUnavailableException e)
                {
                    // If the store renewed it after all, its webhook or the next claim brings the plan back.
                    log.LogWarning(e, "Could not check {Provider} subscription {Id}", s.Provider, s.Id);
                }
            }
            // Whatever the store said, a row still past its end no longer pays for anything.
            await db.Subscriptions
                .Where(x => x.Id == s.Id && x.PeriodEnd <= now && (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Grace))
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, SubscriptionStatus.Expired).SetProperty(x => x.UpdatedAt, now), ct);
        }

        // Every couple whose plan may have changed, including any left on a paid plan with nothing paying for it.
        var stale = await db.Couples.Where(c => c.Plan != CouplePlan.Free
                && !db.Subscriptions.Any(s => s.CoupleId == c.Id && s.Tier == c.Plan
                    && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Grace) && s.PeriodEnd > now))
            .Select(c => c.Id).ToListAsync(ct);
        foreach (var id in stale.Union(due.Select(s => s.CoupleId))) await RecomputeAsync(id, ct);
        return due.Count;
    }
}

public sealed class BillingScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<BillingScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15), clock);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BillingService>().RunExpiryAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Subscription expiry failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
