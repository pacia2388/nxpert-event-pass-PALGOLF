namespace EventRegistration.Api.Services.Theming;

/// <param name="Name">The built-in theme it starts from (PALGOLF or NXPass).</param>
/// <param name="Source">"file" (the theme file beside the API) or "default" (no file).</param>
/// <param name="Customised">True when the theme file changes values of its base.</param>
public record ThemeDto(
    string Name, string Source, bool Customised, string? AppName, string? FooterText,
    ThemeColors Colors, ThemeGradients Gradients, ThemeFonts Fonts, ThemeLayout Layout,
    ThemeLogos Logos, ThemeLogin Login)
{
    public static ThemeDto From(ThemeSnapshot s) => new(
        s.Theme.Base, s.Source, s.Customised, s.Theme.AppName, s.Theme.FooterText,
        s.Theme.Colors, s.Theme.Gradients, s.Theme.Fonts, s.Theme.Layout, s.Theme.Logos, s.Theme.Login);
}
