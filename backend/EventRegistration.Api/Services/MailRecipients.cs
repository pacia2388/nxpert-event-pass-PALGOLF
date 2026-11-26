using System.Text.RegularExpressions;
using EventRegistration.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

/// <summary>
/// A <c>*@domain</c> entry stands for every active user whose address ends with that domain.
/// Expanded server-side; the pattern itself is never sent to the mail server. External
/// distribution lists are not expanded (the app has no directory access).
/// </summary>
public static class MailRecipients
{
    private static readonly Regex DomainPattern = new(@"^\*@[a-z0-9.-]+\.[a-z]{2,}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static bool IsDomainPattern(string? raw) =>
        DomainPattern.IsMatch((raw ?? "").Trim().ToLowerInvariant());

    public static string NormalizePattern(string raw) => raw.Trim().ToLowerInvariant();

    /// <summary>Every active user whose email ends with <c>@domain</c>.</summary>
    public static async Task<List<string>> DomainEmailsAsync(AppDbContext db, string pattern)
    {
        if (!IsDomainPattern(pattern)) return [];
        var domain = NormalizePattern(pattern)[1..]; // "@domain"
        return await db.Users.AsNoTracking()
            .Where(u => u.DeactivatedAt == null && u.Email != "" && u.Email.ToLower().EndsWith(domain))
            .OrderBy(u => u.Email)
            .Select(u => u.Email)
            .ToListAsync();
    }

    /// <summary>
    /// Splits one send into SMTP messages of at most <paramref name="size"/> addresses each
    /// (To/BCC + CC together). CC goes out first, in its own chunks when it alone exceeds the size.
    /// </summary>
    public static List<(List<T> Main, List<T> Cc)> Batches<T>(IReadOnlyList<T> main, IReadOnlyList<T> cc, int size)
    {
        var batches = new List<(List<T>, List<T>)>();
        int m = 0, c = 0;
        while (m < main.Count || c < cc.Count)
        {
            var ccPart = cc.Skip(c).Take(size).ToList();
            c += ccPart.Count;
            var mainPart = main.Skip(m).Take(size - ccPart.Count).ToList();
            m += mainPart.Count;
            batches.Add((mainPart, ccPart));
        }
        return batches;
    }

    /// <summary>Addresses with every <c>*@domain</c> entry expanded, de-duplicated in input order.</summary>
    public static async Task<List<string>> ExpandAsync(AppDbContext db, IEnumerable<string>? values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values ?? [])
        {
            if (IsDomainPattern(raw))
            {
                foreach (var email in await DomainEmailsAsync(db, raw))
                    if (seen.Add(email)) result.Add(email);
                continue;
            }
            if (InvitationMessageBuilder.Address(raw) is { } address && seen.Add(address)) result.Add(address);
        }
        return result;
    }
}
