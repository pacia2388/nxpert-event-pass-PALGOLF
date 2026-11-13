using EventRegistration.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

/// <summary>
/// Mobile numbers, stored everywhere as "+" + country code + number (8–15 digits).
/// Philippine numbers keep the mobile rule "+639XXXXXXXXX".
/// </summary>
public static class PhoneNumber
{
    public const string InvalidMessage = "Please enter a valid mobile number with its country code (e.g. +63 917 123 4567).";
    public const string UnsupportedMessage = "Mobile numbers from this country are not supported.";

    // "+<digits>" = any country; without "+" the input is read as a PH number
    // (09XXXXXXXXX, 639XXXXXXXXX, 9XXXXXXXXX). Spaces, dashes and brackets are ignored.
    // Returns null when the number is not valid.
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (raw.TrimStart().StartsWith('+'))
        {
            if (digits.StartsWith("63")) return Ph(digits[2..]);
            return digits.Length is >= 8 and <= 15 && digits[0] != '0' ? "+" + digits : null;
        }
        if (digits.StartsWith("63")) digits = digits[2..];
        else if (digits.StartsWith("0")) digits = digits[1..];
        return Ph(digits);
    }

    private static string? Ph(string subscriber) =>
        subscriber.Length == 10 && subscriber[0] == '9' ? "+63" + subscriber : null;

    // Every phone entry form (user master, My Account, sign-up, guest registration): a valid number
    // whose country code is in the Phone country table.
    // Blank = (null, null). CSV import uses Normalize only (any country allowed there).
    public static async Task<(string? Phone, string? Error)> ValidateAsync(AppDbContext db, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var phone = Normalize(raw);
        if (phone is null) return (null, InvalidMessage);
        var codes = await db.PhoneCountries.Select(c => c.CountryCode).ToListAsync();
        return Supported(phone, codes) ? (phone, null) : (null, UnsupportedMessage);
    }

    // True when the stored number starts with one of the country codes (digits, no "+").
    // Country codes never prefix one another, so a prefix match is exact.
    public static bool Supported(string? phone, IEnumerable<string> countryCodes)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return countryCodes.Any(c => c.Length > 0 && digits.StartsWith(c));
    }
}
