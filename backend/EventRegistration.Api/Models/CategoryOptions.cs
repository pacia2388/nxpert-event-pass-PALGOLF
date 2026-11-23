using Microsoft.Extensions.Options;

namespace EventRegistration.Api.Models;

// The "Categories" section of categories.json: who events are for and what teams are. Each client
// sets its own list; an empty list (or no file) turns categories off.
public class CategoryOptions
{
    public const int MaxNameLength = 20;

    public List<CategoryItem> Items { get; set; } = [];

    // Team cap for a category without TeamMaxMembers, a category no longer listed, or no category.
    public int DefaultTeamMaxMembers { get; set; } = 10;

    // What is wrong with the file, one line per problem (empty when it is fine).
    public List<string> Errors()
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            var name = item.Name?.Trim() ?? "";
            if (name.Length == 0) errors.Add("A category name is blank.");
            else if (name.Length > MaxNameLength)
                errors.Add($"Category \"{name}\" is longer than {MaxNameLength} characters.");
            else if (!seen.Add(name)) errors.Add($"Category \"{name}\" is listed more than once.");
            if (item.TeamMaxMembers is < 1)
                errors.Add($"Category \"{name}\": TeamMaxMembers must be at least 1.");
        }
        if (DefaultTeamMaxMembers < 1) errors.Add("DefaultTeamMaxMembers must be at least 1.");
        return errors;
    }
}

public class CategoryItem
{
    public string Name { get; set; } = "";
    // Material icon name shown on the category pill; blank = "label".
    public string? Icon { get; set; }
    // Most members a team in this category holds (the Captain is not counted); blank = the default.
    public int? TeamMaxMembers { get; set; }
}

// Checks categories.json at startup and on every change, so a bad edit is reported by name.
public class CategoryOptionsValidator : IValidateOptions<CategoryOptions>
{
    public ValidateOptionsResult Validate(string? name, CategoryOptions options)
    {
        var errors = options.Errors();
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors.Select(e => $"categories.json: {e}"));
    }
}
