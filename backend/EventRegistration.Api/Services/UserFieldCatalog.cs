using EventRegistration.Api.Models;
using Microsoft.Extensions.Options;

namespace EventRegistration.Api.Services;

public record UserFieldDto(string Name, string Label, bool Enabled, int MaxLength);

// Which optional user fields this installation shows (user-fields.json). Read fresh on each call,
// so an edit applies without a restart. A disabled field is ignored on input and left out of
// output, so existing values are never overwritten or cleared.
public class UserFieldCatalog(IOptionsMonitor<UserFieldOptions> options)
{
    private UserFieldOptions Current => options.CurrentValue;

    public bool DepartmentEnabled => Current.Department.Enabled;
    public bool JobRoleEnabled => Current.JobRole.Enabled;

    public List<UserFieldDto> List() =>
    [
        new("Department", "Department", DepartmentEnabled, UserFieldOptions.DepartmentMaxLength),
        new("JobRole", "Job role", JobRoleEnabled, UserFieldOptions.JobRoleMaxLength),
    ];

    public string? CleanDepartment(string? value, out string? error) =>
        Clean(value, DepartmentEnabled, UserFieldOptions.DepartmentMaxLength, "Department", out error);

    public string? CleanJobRole(string? value, out string? error) =>
        Clean(value, JobRoleEnabled, UserFieldOptions.JobRoleMaxLength, "Job role", out error);

    // Checked here (after trimming, only while the field is on) instead of on the request DTO.
    private static string? Clean(string? value, bool enabled, int maxLength, string label, out string? error)
    {
        error = null;
        if (!enabled) return null; // ignored: the caller keeps any existing value
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength)
        {
            error = $"{label} must be {maxLength} characters or fewer.";
            return null;
        }
        return trimmed;
    }
}
