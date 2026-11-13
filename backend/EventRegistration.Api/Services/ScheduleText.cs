using System.Globalization;

namespace EventRegistration.Api.Services;

/// <summary>
/// The schedule wording used in every message and page: the start on one line and the end on
/// the next — "Starts: Thu, Nov 12 2026, 9:00 AM" / "Ends: Sat, Nov 14 2026, 5:30 PM".
/// </summary>
public static class ScheduleText
{
    private const string Format = "ddd, MMM d yyyy, h:mm tt";

    public static string When(DateTime value) => value.ToString(Format, CultureInfo.InvariantCulture);

    public static string Starts(DateTime start) => $"Starts: {When(start)}";

    public static string Ends(DateTime end) => $"Ends: {When(end)}";

    /// <summary>Plain text (SMS / Viber): "Starts: …" then "Ends: …" on the next line.</summary>
    public static string Text(DateTime start, DateTime end) => $"{Starts(start)}\n{Ends(end)}";
}
