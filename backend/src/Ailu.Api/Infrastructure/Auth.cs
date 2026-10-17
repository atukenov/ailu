using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ailu.Api.Infrastructure;

public static class PhoneNumber
{
    // Kazakhstan numbers only for now: +7 followed by ten digits. Accepts the local 8-prefix
    // and any spacing or punctuation people type. Returns null when it is not a KZ mobile number.
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 11 && (digits[0] == '8' || digits[0] == '7')) digits = digits[1..];
        if (digits.Length != 10 || digits[0] != '7') return null;
        return "+7" + digits;
    }
}

public static class Codes
{
    public static string SixDigits() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Hash(string scope, string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope + ":" + secret)));

    public static string OpaqueToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public interface ISmsSender
{
    Task SendAsync(string phone, string text, CancellationToken ct = default);
}

// Until an SMS provider is chosen, codes go to the log. Never enable this in production.
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> log) : ISmsSender
{
    public Task SendAsync(string phone, string text, CancellationToken ct = default)
    {
        log.LogWarning("SMS to {Phone}: {Text}", phone, text);
        return Task.CompletedTask;
    }
}

public sealed class AuthOptions
{
    public string Issuer { get; set; } = "ailu";
    public string Audience { get; set; } = "ailu-app";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 60;
    public int PhoneCodeMinutes { get; set; } = 5;
    public int PhoneCodeResendSeconds { get; set; } = 60;
    public int PhoneCodeMaxAttempts { get; set; } = 5;
    public int InvitationHours { get; set; } = 24;

    // Store review sign-in (see DemoCouple). Off unless both are set.
    public string ReviewPhone { get; set; } = "";
    public string ReviewCode { get; set; } = "";

    public string? ReviewNumber => ReviewPhone.Length > 0 && ReviewCode.Length > 0 ? PhoneNumber.Normalize(ReviewPhone) : null;

    public void Validate()
    {
        if (SigningKey.Length < 32)
            throw new InvalidOperationException("Auth:SigningKey must be set to at least 32 characters.");
        if (ReviewPhone.Length > 0 || ReviewCode.Length > 0)
        {
            if (PhoneNumber.Normalize(ReviewPhone) is null)
                throw new InvalidOperationException("Auth:ReviewPhone must be a Kazakhstan number like +77001234567.");
            if (ReviewCode.Length != 6 || !ReviewCode.All(char.IsAsciiDigit))
                throw new InvalidOperationException("Auth:ReviewCode must be six digits.");
        }
    }

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public sealed class TokenIssuer(AuthOptions options, TimeProvider clock)
{
    public (string Token, DateTimeOffset ExpiresAt) Access(Guid userId)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(options.AccessTokenMinutes);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(options.Key(), SecurityAlgorithms.HmacSha256)
        });
        return (token, expires);
    }
}
