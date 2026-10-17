using Ailu.Api.Data;

namespace Ailu.Api.Domain;

// Photo quotas from the roadmap (document 7 overrides the "unlimited" Premium of document 4).
// One processed photo is about 1.25 MB, so 1 GB is about 800 photos.
public static class Plans
{
    public const long BytesPerPhoto = 1_250_000;

    public static long QuotaBytes(CouplePlan plan) => plan switch
    {
        CouplePlan.Plus => 10_000_000_000,
        CouplePlan.Premium => 30_000_000_000,
        _ => 1_000_000_000
    };
}
