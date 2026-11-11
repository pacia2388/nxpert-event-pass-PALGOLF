namespace EventRegistration.Api.Services;

/// <summary>
/// The app's name as people see it on what the API sends out (email footer, calendar invites).
/// Set once at startup from <c>App:Name</c>; keep it the same as <c>appName</c> in the web app's
/// appSettings.json.
/// </summary>
public static class Branding
{
    public const string DefaultAppName = "PAL GOLF";

    private static string _appName = DefaultAppName;

    /// <summary>Blank or missing keeps the built-in name.</summary>
    public static string AppName
    {
        get => _appName;
        set => _appName = string.IsNullOrWhiteSpace(value) ? DefaultAppName : value.Trim();
    }
}
