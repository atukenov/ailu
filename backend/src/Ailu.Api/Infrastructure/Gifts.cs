using System.Security.Cryptography;
using Ailu.Api.Data;

namespace Ailu.Api.Infrastructure;

// A gift subscription for sale: a plan for a fixed number of months, paid once, never renewed.
public sealed class GiftProduct
{
    public string Id { get; set; } = "";
    public CouplePlan Tier { get; set; }
    public int Months { get; set; }
    public int PriceKzt { get; set; }

    // One, three and twelve months of each plan. Up to three months cost what the months would; a year
    // costs what the yearly plan does. Prices follow the subscription prices until Lev sets their own.
    public static List<GiftProduct> Defaults(IEnumerable<BillingProduct> products)
    {
        var list = new List<GiftProduct>();
        foreach (var tier in new[] { CouplePlan.Plus, CouplePlan.Premium })
        {
            var monthly = products.First(p => p.Tier == tier && p.Period == "month").PriceKzt;
            var yearly = products.FirstOrDefault(p => p.Tier == tier && p.Period == "year")?.PriceKzt ?? BillingOptions.YearlyPrice(monthly);
            var name = tier.ToString().ToLowerInvariant();
            list.Add(new() { Id = $"{name}_1m", Tier = tier, Months = 1, PriceKzt = monthly });
            list.Add(new() { Id = $"{name}_3m", Tier = tier, Months = 3, PriceKzt = monthly * 3 });
            list.Add(new() { Id = $"{name}_12m", Tier = tier, Months = 12, PriceKzt = yearly });
        }
        return list;
    }
}

public enum GiftPaymentStatus { Pending, Paid, Cancelled, Refunded }

// Where the buyer pays: the provider's id for the payment and the page to pay on. No page means there is
// nothing to open (the fake checkout, which is paid at once).
public sealed record GiftCheckoutSession(string CheckoutId, string? PaymentUrl);

// The payment side of gifts. Gifts are paid outside the App Store and Google Play (Kaspi on the website,
// monetization doc); the real provider plugs in here once the merchant account exists. As with the
// stores, the provider is asked about a payment, never told by the app that it went through.
public interface IGiftCheckout
{
    // Saved on each order, so a later provider change still finds old payments.
    string Provider { get; }

    // False while no provider is set up; the app then doesn't offer gifts for sale.
    bool Available { get; }

    Task<GiftCheckoutSession> StartAsync(GiftOrder order, CancellationToken ct = default);

    // Null when the provider doesn't know the payment. Throws StoreUnavailableException when it can't be asked.
    Task<GiftPaymentStatus?> FetchAsync(string checkoutId, CancellationToken ct = default);
}

// Until a payment provider is configured: gifts aren't sold, and anything that asks says so clearly.
public sealed class UnconfiguredGiftCheckout : IGiftCheckout
{
    public string Provider => "none";
    public bool Available => false;

    public Task<GiftCheckoutSession> StartAsync(GiftOrder order, CancellationToken ct = default) =>
        throw new StoreUnavailableException("Gift payments are not configured on this server.");

    public Task<GiftPaymentStatus?> FetchAsync(string checkoutId, CancellationToken ct = default) =>
        throw new StoreUnavailableException("Gift payments are not configured on this server.");
}

// Development only (Billing:UseFakeStore): every gift is paid the moment it is ordered.
public sealed class FakeGiftCheckout : IGiftCheckout
{
    public string Provider => "fake";
    public bool Available => true;

    public Task<GiftCheckoutSession> StartAsync(GiftOrder order, CancellationToken ct = default) =>
        Task.FromResult(new GiftCheckoutSession($"fake-{order.Id:N}", null));

    public Task<GiftPaymentStatus?> FetchAsync(string checkoutId, CancellationToken ct = default) =>
        Task.FromResult<GiftPaymentStatus?>(checkoutId.StartsWith("fake-") ? GiftPaymentStatus.Paid : null);
}

public static class PlanCodes
{
    // Crockford's alphabet: no I, L, O or U, so a code read aloud or retyped from a photo survives.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    // Twelve characters, 60 random bits: guessing a gift code is out of reach without any rate limit.
    public static string NewGiftCode()
    {
        Span<char> chars = stackalloc char[12];
        for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    // What people type: any case, with spaces or dashes. Promo codes may be any letters ("LOVE2026", "АЙЛУ").
    public static string Normalize(string code) =>
        new(code.Trim().ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    public static bool IsValid(string normalized) => normalized.Length is >= 4 and <= 32 && normalized.All(char.IsLetterOrDigit);

    // A gift code retyped with O for zero or I/L for one. Gift codes never contain those letters, so this
    // is only tried when the code as typed matches nothing.
    public static string AsGiftCode(string normalized) =>
        new(normalized.Select(c => c switch { 'O' => '0', 'I' or 'L' => '1', _ => c }).ToArray());

    // Gift codes are shown in groups of four: 7KQ2-M9XD-4HPA.
    public static string Display(string code, PlanCodeKind kind) =>
        kind == PlanCodeKind.Gift && code.Length == 12 ? $"{code[..4]}-{code[4..8]}-{code[8..]}" : code;
}
