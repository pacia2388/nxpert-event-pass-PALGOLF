using System.Globalization;

namespace EventRegistration.Api.Services;

/// <summary>Birthdate rules shared by admin user create/update, profile update, sign-up and CSV import.</summary>
public static class BirthdateRules
{
    private static readonly DateOnly Min = new(1900, 1, 1);
    private static readonly string[] CsvFormats = { "yyyy-MM-dd", "M/d/yyyy" };

    // Returns an error message, or null when the birthdate is present and plausible.
    public static string? Validate(DateOnly? birthdate)
    {
        if (birthdate is null) return "Birthdate is required.";
        if (birthdate < Min) return "Birthdate must be on or after 1900-01-01.";
        if (birthdate > DateOnly.FromDateTime(DateTime.Today)) return "Birthdate cannot be in the future.";
        return null;
    }

    // CSV cells: ISO (yyyy-MM-dd) or Excel's default M/d/yyyy. Blank or unparseable = null.
    public static DateOnly? ParseCsv(string? raw) =>
        DateOnly.TryParseExact((raw ?? "").Trim(), CsvFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;
}
