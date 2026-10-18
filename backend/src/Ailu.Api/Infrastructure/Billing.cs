using Ailu.Api.Data;

namespace Ailu.Api.Infrastructure;

// A product the couple can subscribe to. Prices live in App Store Connect and Google Play; the app shows
// the store's own price string. PriceKzt is only for the app to show before the store answers.
public sealed class BillingProduct
{
    public string Id { get; set; } = "";
    public CouplePlan Tier { get; set; }
    public string Period { get; set; } = "month";
    public int PriceKzt { get; set; }
    public string AppleProductId { get; set; } = "";
    public string GoogleProductId { get; set; } = "";
    public string GoogleBasePlanId { get; set; } = "";
}

// Something bought once rather than subscribed to, like the video story of the year. On both stores it is a
// consumable, so the couple can buy it again for the next year.
public sealed class OneTimeProduct
{
    public string Id { get; set; } = "";
    public int PriceKzt { get; set; }
    public string AppleProductId { get; set; } = "";
    public string GoogleProductId { get; set; } = "";

    public string StoreProductId(StoreProvider provider) => provider == StoreProvider.Apple ? AppleProductId : GoogleProductId;
}

public sealed class AppleStoreOptions
{
    public string BundleId { get; set; } = "kz.ailu.app";
    public string IssuerId { get; set; } = "";
    public string KeyId { get; set; } = "";

    // The App Store Connect API key (.p8 contents, PEM).
    public string PrivateKey { get; set; } = "";

    public bool Configured => IssuerId != "" && KeyId != "" && PrivateKey != "";
}

public sealed class GoogleStoreOptions
{
    public string PackageName { get; set; } = "kz.ailu.app";

    // A service account with access to the Play Console, as the JSON key file Google issues.
    public string ServiceAccountJson { get; set; } = "";

    // Real-time developer notifications arrive from a Pub/Sub push subscription whose URL carries this.
    public string WebhookSecret { get; set; } = "";

    public bool Configured => ServiceAccountJson != "";
}

public sealed class BillingOptions
{
    public const int TrialDays = 7;

    // Placeholder prices from the monetization doc (Plus 1 490 ₸, Premium 2 490 ₸ a month). Lev has not
    // set prices yet; change them here (or in config under Billing:Products) and in both stores.
    // A year costs twelve months less 20% (Lev, 2026-10-17). On Google both periods are base plans of
    // one subscription; Apple needs a product for each.
    // Config binding appends to a list, so these apply only when config names no products.
    public static List<BillingProduct> DefaultProducts() =>
    [
        new()
        {
            Id = "plus_monthly", Tier = CouplePlan.Plus, Period = "month", PriceKzt = 1490,
            AppleProductId = "kz.ailu.plus.monthly", GoogleProductId = "plus", GoogleBasePlanId = "monthly"
        },
        new()
        {
            Id = "premium_monthly", Tier = CouplePlan.Premium, Period = "month", PriceKzt = 2490,
            AppleProductId = "kz.ailu.premium.monthly", GoogleProductId = "premium", GoogleBasePlanId = "monthly"
        },
        new()
        {
            Id = "plus_yearly", Tier = CouplePlan.Plus, Period = "year", PriceKzt = YearlyPrice(1490),
            AppleProductId = "kz.ailu.plus.yearly", GoogleProductId = "plus", GoogleBasePlanId = "yearly"
        },
        new()
        {
            Id = "premium_yearly", Tier = CouplePlan.Premium, Period = "year", PriceKzt = YearlyPrice(2490),
            AppleProductId = "kz.ailu.premium.yearly", GoogleProductId = "premium", GoogleBasePlanId = "yearly"
        }
    ];

    public const int YearlyDiscountPercent = 20;

    public static int YearlyPrice(int monthly) => monthly * 12 * (100 - YearlyDiscountPercent) / 100;

    public List<BillingProduct> Products { get; set; } = [];

    // The video story of the year, 1 990 ₸ (roadmap, phase 4). A placeholder price like the plans'.
    public OneTimeProduct YearStory { get; set; } = new()
    {
        Id = "year_story", PriceKzt = 1990, AppleProductId = "kz.ailu.story.year", GoogleProductId = "year_story"
    };

    public AppleStoreOptions Apple { get; set; } = new();
    public GoogleStoreOptions Google { get; set; } = new();

    // Development only: accept made-up purchases ("fake:<product id>") so the app can be tried without
    // a store. Refused at startup outside Development and Testing.
    public bool UseFakeStore { get; set; }

    public CouplePlan? TierOf(StoreProvider provider, string productId) => Products.FirstOrDefault(p => provider switch
    {
        StoreProvider.Apple => p.AppleProductId == productId,
        StoreProvider.Google => p.GoogleProductId == productId,
        _ => false
    })?.Tier;
}

// What a store says about one subscription, already checked against the store's own servers.
public sealed record StorePurchase(
    StoreProvider Provider,
    string StoreId,
    string ProductId,
    SubscriptionStatus Status,
    DateTimeOffset PeriodEnd,
    bool AutoRenew,
    bool Sandbox,
    // The user id the app attached at purchase (Apple appAccountToken, Google obfuscated account id).
    string? AccountId = null,
    // Google: the purchase token this one replaced on an upgrade, downgrade or resubscribe.
    string? ReplacesStoreId = null,
    bool NeedsAcknowledge = false);

// What a store says about one purchase of a one-time product. Paid is false for a refunded, cancelled or
// still pending purchase.
public sealed record StoreProductPurchase(
    StoreProvider Provider,
    string StoreId,
    string ProductId,
    bool Paid,
    bool Sandbox,
    string? AccountId = null,
    bool NeedsAcknowledge = false);

public interface IStoreClient
{
    StoreProvider Provider { get; }

    // Null when the store does not know the purchase. Throws StoreUnavailableException when it can't be asked.
    Task<StorePurchase?> FetchAsync(string storeId, CancellationToken ct = default);

    Task AcknowledgeAsync(StorePurchase purchase, CancellationToken ct = default);

    // A one-time purchase. Google looks purchases up by product, so the product id comes along.
    Task<StoreProductPurchase?> FetchProductAsync(string productId, string storeId, CancellationToken ct = default);

    Task AcknowledgeProductAsync(StoreProductPurchase purchase, CancellationToken ct = default);
}

public sealed class StoreUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

// Stands in for a store whose credentials are not set, so the API still starts and says so clearly.
public sealed class UnconfiguredStore(StoreProvider provider) : IStoreClient
{
    public StoreProvider Provider => provider;

    public Task<StorePurchase?> FetchAsync(string storeId, CancellationToken ct = default) =>
        throw new StoreUnavailableException($"{provider} purchases are not configured on this server.");

    public Task AcknowledgeAsync(StorePurchase purchase, CancellationToken ct = default) => Task.CompletedTask;

    public Task<StoreProductPurchase?> FetchProductAsync(string productId, string storeId, CancellationToken ct = default) =>
        throw new StoreUnavailableException($"{provider} purchases are not configured on this server.");

    public Task AcknowledgeProductAsync(StoreProductPurchase purchase, CancellationToken ct = default) => Task.CompletedTask;
}

// Development only (Billing:UseFakeStore). "fake:kz.ailu.plus.monthly" is an active monthly purchase from now on
// ("...yearly" a yearly one); "fake-expired:..." one that has ended. One-time products work the same way:
// "fake:kz.ailu.story.year" is paid, "fake-expired:..." refunded.
public sealed class FakeStore(StoreProvider provider, TimeProvider clock) : IStoreClient
{
    public StoreProvider Provider => provider;

    public Task<StorePurchase?> FetchAsync(string storeId, CancellationToken ct = default)
    {
        var parts = storeId.Split(':', 3);
        if (parts.Length < 2 || parts[0] is not ("fake" or "fake-expired")) return Task.FromResult<StorePurchase?>(null);
        var expired = parts[0] == "fake-expired";
        var now = clock.GetUtcNow();
        return Task.FromResult<StorePurchase?>(new StorePurchase(provider, storeId, parts[1],
            expired ? SubscriptionStatus.Expired : SubscriptionStatus.Active,
            expired ? now.AddDays(-1) : parts[1].EndsWith("yearly") ? now.AddYears(1) : now.AddMonths(1), !expired, Sandbox: true));
    }

    public Task AcknowledgeAsync(StorePurchase purchase, CancellationToken ct = default) => Task.CompletedTask;

    public Task<StoreProductPurchase?> FetchProductAsync(string productId, string storeId, CancellationToken ct = default)
    {
        var parts = storeId.Split(':', 3);
        if (parts.Length < 2 || parts[0] is not ("fake" or "fake-expired") || parts[1] != productId)
            return Task.FromResult<StoreProductPurchase?>(null);
        return Task.FromResult<StoreProductPurchase?>(new StoreProductPurchase(provider, storeId, productId, parts[0] == "fake", Sandbox: true));
    }

    public Task AcknowledgeProductAsync(StoreProductPurchase purchase, CancellationToken ct = default) => Task.CompletedTask;
}
