namespace EventRegistration.Api.Services.Theming;

/// <summary>
/// A look for the app: colours, gradients, fonts, layout variants, logos and the login poster
/// caption. Serialized camelCase, this is also the theme.json format (see documentation/theming.md).
/// </summary>
public sealed class ThemeDefinition
{
    public int Version { get; init; } = 1;
    /// <summary>The built-in theme this one starts from: PALGOLF or NXPass.</summary>
    public string Base { get; init; } = BuiltInThemes.PalGolfName;
    /// <summary>Overrides the app name (web appSettings.json, API App:Name) when set.</summary>
    public string? AppName { get; init; }
    /// <summary>Overrides the web app's footer text when set.</summary>
    public string? FooterText { get; init; }
    public ThemeColors Colors { get; init; } = new();
    public ThemeGradients Gradients { get; init; } = new();
    public ThemeFonts Fonts { get; init; } = new();
    public ThemeLayout Layout { get; init; } = new();
    public ThemeLogos Logos { get; init; } = new();
    public ThemeLogin Login { get; init; } = new();
}

public sealed class ThemeColors
{
    public string Primary { get; init; } = "";
    public string PrimaryDeep { get; init; } = "";
    public string PrimaryContainer { get; init; } = "";
    public string OnPrimaryContainer { get; init; } = "";
    public string SecondaryContainer { get; init; } = "";
    public string OnSecondaryContainer { get; init; } = "";
    public string Tertiary { get; init; } = "";
    public string Navy { get; init; } = "";
    public string NavyMid { get; init; } = "";
    public string Sky { get; init; } = "";
    public string Accent { get; init; } = "";
    public string Gold { get; init; } = "";
    public string Ink { get; init; } = "";
    public string InkSoft { get; init; } = "";
    public string Line { get; init; } = "";
    public string AppBackground { get; init; } = "";
    public string SurfaceLow { get; init; } = "";
    public string SurfaceHigh { get; init; } = "";
    public string SurfaceHighest { get; init; } = "";
    public string Tint { get; init; } = "";
    public string TintSoft { get; init; } = "";
    /// <summary>Phone browser toolbar colour (the theme-color meta tag).</summary>
    public string BrowserBar { get; init; } = "";
}

public sealed class ThemeGradients
{
    public string Brand { get; init; } = "";
    public string BrandSoft { get; init; } = "";
    public string Header { get; init; } = "";
    public string Progress { get; init; } = "";
    /// <summary>Filled buttons, when layout.buttons is "gradient".</summary>
    public string Button { get; init; } = "";
}

public sealed class ThemeFonts
{
    public string Display { get; init; } = "";
}

public sealed class ThemeLayout
{
    /// <summary>"solid" or "gradient" filled buttons.</summary>
    public string Buttons { get; init; } = "solid";
    /// <summary>"solid" (navy bar) or "glass" (frosted white) footer.</summary>
    public string Footer { get; init; } = "solid";
    /// <summary>"gradient" (navy sky) or "glow" (light with soft glows) behind sign-up pages.</summary>
    public string AuthBackground { get; init; } = "gradient";
    /// <summary>
    /// "poster" (the image fills the panel; its words are baked in) or "logo" (a logo centred on a
    /// light panel, with the login title and lines shown as text).
    /// </summary>
    public string LoginImage { get; init; } = "poster";
}

/// <summary>Image paths relative to the web app's root, or https URLs.</summary>
public sealed class ThemeLogos
{
    public string Favicon { get; init; } = "";
    public string TabIcon { get; init; } = "";
    /// <summary>Header logo; null shows the built-in badge icon.</summary>
    public string? Header { get; init; }
    public string LoginImage { get; init; } = "";
}

/// <summary>The words that go with the login image (shown on phones, read by screen readers).</summary>
public sealed class ThemeLogin
{
    public string ImageAlt { get; init; } = "";
    public string Title { get; init; } = "";
    public List<string> Lines { get; init; } = [];
    public string Caption { get; init; } = "";
}
