using System.Security.Cryptography;
using System.Text;

namespace Ailu.Api.Infrastructure;

// The team's own calls (/v1/admin/...): the X-Admin-Key header must match Billing:AdminKey. An empty key
// turns them all off.
public static class AdminKey
{
    public static bool Allows(HttpContext http, BillingOptions options)
    {
        var expected = options.AdminKey;
        var given = http.Request.Headers["X-Admin-Key"].ToString();
        return expected != "" && given != ""
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
    }
}
