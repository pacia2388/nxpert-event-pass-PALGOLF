using System.Text.RegularExpressions;

namespace EventRegistration.Api.Services;

/// <summary>
/// Small rules shared by every place that creates accounts (admin Users page, CSV import, a Team
/// Lead adding a member).
/// </summary>
public static class UserAccountHelper
{
    // Compose "First M. Last" from the captured parts (middle initial optional).
    public static string ComposeFullName(string first, string? middle, string last)
    {
        var mi = (middle ?? "").Trim().TrimEnd('.');
        var parts = new List<string> { first.Trim() };
        if (mi.Length > 0) parts.Add(mi + ".");
        parts.Add(last.Trim());
        return string.Join(" ", parts);
    }

    public static bool IsValidEmail(string email) =>
        Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");

    public static bool HasControlChars(string? value) =>
        value is not null && value.Any(char.IsControl);
}
