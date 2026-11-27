using System.Security.Cryptography;
using Ailu.Api.Data;

namespace Ailu.Api.Infrastructure;

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
