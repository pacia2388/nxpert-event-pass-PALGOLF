using System.Diagnostics.CodeAnalysis;
using EventRegistration.Api.Models;
using Microsoft.Extensions.Options;

namespace EventRegistration.Api.Services;

public record CategoryDto(string Name, string Icon, int TeamMaxMembers);

// The configured categories (categories.json), read fresh on each call so an edit applies without a
// restart. Events and teams share the list: the register warning compares the two.
public class CategoryCatalog(IOptionsMonitor<CategoryOptions> options)
{
    private const string DefaultIcon = "label";

    private CategoryOptions Current => options.CurrentValue;

    public bool Enabled => Current.Items.Count > 0;

    // "Senior, Regular", for error messages.
    public string Names => string.Join(", ", Current.Items.Select(c => c.Name.Trim()));

    public List<CategoryDto> List() => Current.Items
        .Select(c => new CategoryDto(
            c.Name.Trim(),
            string.IsNullOrWhiteSpace(c.Icon) ? DefaultIcon : c.Icon.Trim(),
            c.TeamMaxMembers ?? Current.DefaultTeamMaxMembers))
        .ToList();

    // Most members a team holds; a category no longer listed, or none, uses the default.
    public int TeamMaxMembers(string? category) =>
        Find(category)?.TeamMaxMembers ?? Current.DefaultTeamMaxMembers;

    // Blank = no category. Otherwise a listed name (saved as listed), or the event's current
    // category even if it was taken off the list since.
    public bool TryEventCategory(string? sent, string? stored, out string? saved)
    {
        var trimmed = sent?.Trim() ?? "";
        saved = null;
        if (trimmed.Length == 0) return true;
        saved = Find(trimmed)?.Name.Trim() ?? Same(trimmed, stored);
        return saved is not null;
    }

    // A listed name (saved as listed), or the team's current category even if it was taken off the
    // list since. With no categories configured a team has none ("").
    public bool TryTeamCategory(string? sent, string? stored, [NotNullWhen(true)] out string? saved)
    {
        var trimmed = sent?.Trim() ?? "";
        saved = Find(trimmed)?.Name.Trim()
            ?? Same(trimmed, stored)
            ?? (trimmed.Length == 0 && !Enabled ? "" : null);
        return saved is not null;
    }

    private CategoryItem? Find(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        return trimmed.Length == 0
            ? null
            : Current.Items.FirstOrDefault(c => string.Equals(c.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Same(string sent, string? stored) =>
        stored is not null && string.Equals(sent, stored, StringComparison.OrdinalIgnoreCase) ? stored : null;
}
