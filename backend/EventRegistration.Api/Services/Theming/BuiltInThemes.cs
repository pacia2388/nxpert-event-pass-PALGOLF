namespace EventRegistration.Api.Services.Theming;

/// <summary>
/// The two looks the app ships with. PALGOLF is the current look (styles.scss defaults); NXPass is
/// the earlier teal look of the nxpert-event-pass app.
/// </summary>
public static class BuiltInThemes
{
    public const string PalGolfName = "PALGOLF";
    public const string NxPassName = "NXPass";

    public static readonly ThemeDefinition PalGolf = new()
    {
        Base = PalGolfName,
        Colors = new()
        {
            Primary = "#0f5fb8",
            PrimaryDeep = "#0b4a94",
            PrimaryContainer = "#d6e4f7",
            OnPrimaryContainer = "#051f63",
            SecondaryContainer = "#e8f0fb",
            OnSecondaryContainer = "#0b4a94",
            Tertiary = "#0a2d7a",
            Navy = "#051f63",
            NavyMid = "#0a2d7a",
            Sky = "#1a5aa8",
            Accent = "#e02a2a",
            Gold = "#e1b115",
            Ink = "#0b1b3f",
            InkSoft = "#46557a",
            Line = "#cdd7e8",
            AppBackground = "#f2f5fa",
            SurfaceLow = "#f8fafd",
            SurfaceHigh = "#f0f4fa",
            SurfaceHighest = "#e6ecf5",
            Tint = "#e8f0fb",
            TintSoft = "#f0f5fc",
            BrowserBar = "#051f63",
        },
        Gradients = new()
        {
            Brand = "linear-gradient(135deg, #0a2d7a 0%, #0f5fb8 100%)",
            BrandSoft = "linear-gradient(135deg, #e8f0fb 0%, #f0f5fc 100%)",
            Header = "linear-gradient(90deg, #051f63 0%, #0a2d7a 55%, #0f5fb8 100%)",
            Progress = "linear-gradient(90deg, #e02a2a, #e8742a 55%, #e1b115)",
            Button = "linear-gradient(135deg, #0a2d7a 0%, #0f5fb8 100%)",
        },
        Fonts = new() { Display = "'Montserrat', 'Roboto', sans-serif" },
        Layout = new() { Buttons = "solid", Footer = "solid", AuthBackground = "gradient" },
        Logos = new()
        {
            Favicon = "favicon.ico?v=3",
            TabIcon = "icons/tab-192.png?v=3",
            Header = null,
            LoginImage = "brand/pal-interclub-2027-poster.jpg",
        },
        Login = new()
        {
            ImageAlt = "Philippine Airlines tournament poster: a golfer mid-swing above a golf course",
            Title = "78th PAL Interclub Golf Tournament 2027",
            Lines = ["Seniors Division: Feb 17–24, 2027", "Men’s/Regular Division: Feb 25–Mar 4, 2027"],
            Caption = "Cagayan de Oro | Bukidnon",
        },
    };

    public static readonly ThemeDefinition NxPass = new()
    {
        Base = NxPassName,
        AppName = "NXPass",
        FooterText = "Powered by @NOW — NXPERT OFFICE WORKSPACE",
        Colors = new()
        {
            Primary = "#0d9488",
            PrimaryDeep = "#0f766e",
            PrimaryContainer = "#ccf2ec",
            OnPrimaryContainer = "#064e46",
            SecondaryContainer = "#e6f6f3",
            OnSecondaryContainer = "#0f766e",
            Tertiary = "#16a34a",
            Navy = "#064e46",
            NavyMid = "#0f766e",
            Sky = "#0d9488",
            Accent = "#3dbb8a",
            Gold = "#7dd36a",
            Ink = "#0f172a",
            InkSoft = "#475569",
            Line = "#cfe3dd",
            AppBackground = "#f1f7f4",
            SurfaceLow = "#f8fbfa",
            SurfaceHigh = "#f1f6f4",
            SurfaceHighest = "#e8efec",
            Tint = "#e6f6f3",
            TintSoft = "#f3fbf8",
            BrowserBar = "#0d9488",
        },
        Gradients = new()
        {
            Brand = "linear-gradient(135deg, #0d9488 0%, #22c55e 100%)",
            BrandSoft = "linear-gradient(135deg, #e6f7f4 0%, #eafaf0 100%)",
            Header = "linear-gradient(90deg, #0d9488 0%, #3dbb8a 55%, #7dd36a 100%)",
            Progress = "linear-gradient(90deg, #0d9488, #3dbb8a 55%, #7dd36a)",
            Button = "linear-gradient(135deg, #0d9488 0%, #22b07d 100%)",
        },
        Fonts = new() { Display = "'Roboto', sans-serif" },
        Layout = new() { Buttons = "gradient", Footer = "glass", AuthBackground = "glow" },
        Logos = new()
        {
            Favicon = "themes/nxpass/favicon.ico",
            TabIcon = "themes/nxpass/icon-192.png",
            Header = null,
            LoginImage = "nxpax-logo.png",
        },
        Login = new()
        {
            ImageAlt = "NXPass logo",
            Title = "NXPass",
            Lines = ["Events, check-ins and passes, all in one place."],
            Caption = "Event Management",
        },
    };

    /// <summary>The built-in theme with this name (ignoring case), or null.</summary>
    public static ThemeDefinition? Find(string? name) =>
        string.Equals(name, PalGolfName, StringComparison.OrdinalIgnoreCase) ? PalGolf
        : string.Equals(name, NxPassName, StringComparison.OrdinalIgnoreCase) ? NxPass
        : null;
}
