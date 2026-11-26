using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

public record MailAudienceGroup(string Id, string Name, string Icon, List<string> Emails);
public record MailRecipient(string Email, int? UserId, string? Phone);

/// <summary>
/// Saved receiver lists for the mail planner, built from live data: every user, admins, each
/// organization and level, and the registrants of recent and upcoming events. A plan stores
/// list ids, so each day goes to the list's members at the time it is sent.
/// </summary>
public class MailPlanAudience(AppDbContext db)
{
    public const string AllUsers = "all";
    public const string Admins = "admins";

    public async Task<List<MailAudienceGroup>> GroupsAsync()
    {
        var users = (await db.Users.AsNoTracking().Where(u => u.Email != "" && u.DeactivatedAt == null)
                .Select(u => new { u.Email, u.Role, u.Organization, u.Level }).ToListAsync())
            .Select(u => new { Email = InvitationMessageBuilder.Address(u.Email), u.Role, Organization = u.Organization?.Trim(), Level = u.Level?.Trim() })
            .Where(u => u.Email != null).ToList();
        List<string> Emails<T>(IEnumerable<T> items, Func<T, string?> email) =>
            items.Select(email).Where(e => e != null).Cast<string>().Distinct().Order().ToList();

        var groups = new List<MailAudienceGroup>
        {
            new(AllUsers, "All users", "groups", Emails(users, u => u.Email)),
            new(Admins, "Admins", "admin_panel_settings", Emails(users.Where(u => u.Role is Roles.Admin or Roles.SuperAdmin), u => u.Email)),
        };
        foreach (var org in users.Where(u => !string.IsNullOrEmpty(u.Organization)).GroupBy(u => u.Organization!, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
            groups.Add(new("org:" + org.Key.ToLowerInvariant(), org.Key, "corporate_fare", Emails(org, u => u.Email)));
        foreach (var level in users.Where(u => !string.IsNullOrEmpty(u.Level)).GroupBy(u => u.Level!, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
            groups.Add(new("level:" + level.Key.ToLowerInvariant(), level.Key, "workspace_premium", Emails(level, u => u.Email)));

        // Registrants (pending or confirmed) of events that haven't been over for more than 30 days.
        var since = DateTime.Now.AddDays(-30);
        var events = await db.Events.AsNoTracking().Where(e => e.EndsAt >= since)
            .OrderBy(e => e.StartsAt).Select(e => new { e.Id, e.Title }).Take(100).ToListAsync();
        var eventIds = events.Select(e => e.Id).ToList();
        var registrants = await db.Registrations.AsNoTracking()
            .Where(r => eventIds.Contains(r.EventId) && (r.User == null || r.User.DeactivatedAt == null))
            .Select(r => new { r.EventId, Email = r.User != null ? r.User.Email : r.GuestEmail }).ToListAsync();
        foreach (var ev in events)
        {
            var emails = Emails(registrants.Where(r => r.EventId == ev.Id), r => InvitationMessageBuilder.Address(r.Email));
            if (emails.Count > 0) groups.Add(new("event:" + ev.Id, ev.Title + " registrants", "how_to_reg", emails));
        }
        return groups;
    }

    /// <summary>Typed addresses with every <c>*@domain</c> entry expanded to active users.</summary>
    public Task<List<string>> ExpandAsync(IEnumerable<string>? emails) => MailRecipients.ExpandAsync(db, emails);

    /// <summary>Unique receivers of a selection, with the account and phone (for Viber) behind each address.</summary>
    public async Task<List<MailRecipient>> ResolveAsync(IEnumerable<string> groupIds, IEnumerable<string> emails,
        IEnumerable<string>? excluded = null)
    {
        var ids = groupIds.ToHashSet();
        // Keep the order the addresses were entered in (CC header shows them as typed).
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count > 0)
            foreach (var group in (await GroupsAsync()).Where(g => ids.Contains(g.Id)))
                list.AddRange(group.Emails.Where(seen.Add));
        foreach (var address in await MailRecipients.ExpandAsync(db, emails))
            if (seen.Add(address)) list.Add(address);
        // Group members ticked off in the plan dialog are never mailed.
        var blocked = (excluded ?? []).Select(InvitationMessageBuilder.Address).Where(e => e != null)
            .Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (blocked.Count > 0) list.RemoveAll(blocked.Contains);
        if (list.Count == 0) return [];

        // Deactivated accounts are never mailed, even when their address is typed or in a group.
        var deactivated = (await db.Users.AsNoTracking()
                .Where(u => u.DeactivatedAt != null && list.Contains(u.Email))
                .Select(u => u.Email).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (deactivated.Count > 0) list.RemoveAll(deactivated.Contains);
        if (list.Count == 0) return [];

        var users = (await db.Users.AsNoTracking().Where(u => list.Contains(u.Email))
                .Select(u => new { u.Id, u.Email, u.Phone }).ToListAsync())
            .GroupBy(u => u.Email, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        // Guests have no account; use the phone from their latest registration.
        var guests = (await db.Registrations.AsNoTracking()
                .Where(r => r.UserId == null && r.GuestEmail != null && list.Contains(r.GuestEmail) && r.GuestPhone != null)
                .OrderByDescending(r => r.RegisteredAt).Select(r => new { r.GuestEmail, r.GuestPhone }).ToListAsync())
            .GroupBy(r => r.GuestEmail!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().GuestPhone, StringComparer.OrdinalIgnoreCase);
        return list.Select(email => users.TryGetValue(email, out var user)
                ? new MailRecipient(email, user.Id, string.IsNullOrWhiteSpace(user.Phone) ? null : user.Phone.Trim())
                : new MailRecipient(email, null, guests.GetValueOrDefault(email)?.Trim()))
            .ToList();
    }
}
