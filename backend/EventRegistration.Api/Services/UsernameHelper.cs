using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

/// <summary>
/// Builds login usernames. The default handle is the first letter of the first name + the last
/// name (e.g. John Doe → "jdoe"), lower-cased and stripped to letters/digits, made unique by
/// appending a number on collision.
/// </summary>
public static class UsernameHelper
{
    // The un-numbered base handle derived from a member's name.
    public static string Base(string? firstName, string? lastName, string? fullName)
    {
        var fn = (firstName ?? "").Trim();
        var ln = (lastName ?? "").Trim();
        var first = fn.Length > 0 ? fn : (fullName ?? "").Trim();
        var initial = first.Length > 0 ? first[0].ToString() : "u";
        var raw = (initial + ln).ToLowerInvariant();
        var cleaned = new string(raw.Where(char.IsLetterOrDigit).ToArray());
        return cleaned.Length > 0 ? cleaned : "user";
    }

    // Normalizes an admin-supplied username: lower-case, letters/digits/._- only.
    public static string Normalize(string username) =>
        new string(username.Trim().ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());

    // Returns a unique username, appending 2, 3, … to the base when taken.
    public static async Task<string> UniqueAsync(AppDbContext db, string baseName, int? excludeUserId = null)
    {
        var name = baseName;
        var n = 1;
        while (await db.Users.AnyAsync(u => u.Username == name && (excludeUserId == null || u.Id != excludeUserId)))
            name = baseName + ++n;
        return name;
    }

    // Saves a newly added user whose username was generated from `baseName`. UniqueAsync checks
    // before the insert, so a concurrent request can take the same name first; on that unique-index
    // clash, pick the next free name and save again.
    public static async Task SaveNewUserAsync(AppDbContext db, User user, string baseName)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException e) when (attempt < 10 && IsUsernameClash(e))
            {
                user.Username = await UniqueAsync(db, baseName);
            }
        }
    }

    private static bool IsUsernameClash(DbUpdateException e) =>
        e.InnerException?.Message.Contains("IX_Users_Username", StringComparison.Ordinal) == true;
}
