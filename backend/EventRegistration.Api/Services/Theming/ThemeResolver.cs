using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EventRegistration.Api.Services.Theming;

/// <param name="Customised">True when the file changed anything beyond picking its base.</param>
public sealed record ResolvedTheme(ThemeDefinition Theme, bool Customised, IReadOnlyList<string> Warnings);

/// <summary>
/// Turns a theme.json into a full theme: <c>base</c> picks a built-in theme and every other key
/// overrides one value of it. Invalid values are dropped with a warning; nothing here ever throws,
/// so a broken file can never stop the app (it falls back to PALGOLF).
/// </summary>
public static partial class ThemeResolver
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Keep "—", "’" and quotes in fonts readable in the downloaded template.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private enum Kind { Color, Gradient, Font, Path, NullablePath, Text, TextList, Choice }

    private sealed record Field(Kind Kind, params string[] Choices);

    private static Dictionary<string, Field> All(Kind kind, ThemeDefinition sample, Func<JsonObject, JsonNode?> section) =>
        section(JsonSerializer.SerializeToNode(sample, Json)!.AsObject())!.AsObject()
            .ToDictionary(p => p.Key, _ => new Field(kind));

    private static readonly Dictionary<string, Dictionary<string, Field>> Sections = new()
    {
        ["colors"] = All(Kind.Color, BuiltInThemes.PalGolf, o => o["colors"]),
        ["gradients"] = All(Kind.Gradient, BuiltInThemes.PalGolf, o => o["gradients"]),
        ["fonts"] = new() { ["display"] = new(Kind.Font) },
        ["layout"] = new()
        {
            ["buttons"] = new(Kind.Choice, "solid", "gradient"),
            ["footer"] = new(Kind.Choice, "solid", "glass"),
            ["authBackground"] = new(Kind.Choice, "gradient", "glow"),
            ["loginImage"] = new(Kind.Choice, "poster", "logo"),
        },
        ["logos"] = new()
        {
            ["favicon"] = new(Kind.Path),
            ["tabIcon"] = new(Kind.Path),
            ["header"] = new(Kind.NullablePath),
            ["loginImage"] = new(Kind.Path),
        },
        ["login"] = new()
        {
            ["imageAlt"] = new(Kind.Text),
            ["title"] = new(Kind.Text),
            ["lines"] = new(Kind.TextList),
            ["caption"] = new(Kind.Text),
        },
    };

    // Keys that describe the file rather than the look.
    private static readonly HashSet<string> Ignored = ["base", "version", "$help", "$schema"];

    public const int MaxText = 200;
    public const int MaxLines = 6;

    /// <summary>
    /// The downloadable theme.json template: the whole PALGOLF theme, every key filled in, with a
    /// pointer to the docs. Saved unchanged as theme.json, it gives exactly the default look.
    /// </summary>
    public static string Template()
    {
        var template = new JsonObject
        {
            ["$help"] = "Save as theme.json in the API site folder (it is never published). \"base\" is PALGOLF or NXPass; "
                + "delete any key to keep the base value. Logo paths are relative to the web app. See documentation/theming.md.",
        };
        foreach (var (key, value) in JsonSerializer.SerializeToNode(BuiltInThemes.PalGolf, Json)!.AsObject())
            template[key] = value?.DeepClone();
        return template.ToJsonString(Json);
    }

    public static ResolvedTheme Resolve(string? json)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
            return new(BuiltInThemes.PalGolf, false, warnings);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: ParseOptions);
        }
        catch (JsonException ex)
        {
            warnings.Add($"not valid JSON ({ex.Message}); using {BuiltInThemes.PalGolfName}.");
            return new(BuiltInThemes.PalGolf, false, warnings);
        }
        if (root is not JsonObject file)
        {
            warnings.Add($"must be a JSON object; using {BuiltInThemes.PalGolfName}.");
            return new(BuiltInThemes.PalGolf, false, warnings);
        }

        var baseName = file["base"] is JsonValue b && b.TryGetValue<string>(out var s) ? s : null;
        var baseTheme = BuiltInThemes.Find(baseName);
        if (baseTheme is null)
        {
            if (file.ContainsKey("base"))
                warnings.Add($"base \"{file["base"]}\" is not {BuiltInThemes.PalGolfName} or {BuiltInThemes.NxPassName}; using {BuiltInThemes.PalGolfName}.");
            baseTheme = BuiltInThemes.PalGolf;
        }
        if (file["version"] is JsonNode v && v.ToJsonString() != "1")
            warnings.Add($"version {v.ToJsonString()} is not supported; reading it as version 1.");

        var merged = JsonSerializer.SerializeToNode(baseTheme, Json)!.AsObject();
        var customised = false;
        foreach (var (key, value) in file)
        {
            if (Ignored.Contains(key)) continue;
            if (key is "appName" or "footerText")
            {
                if (value is null || TryText(value, out _))
                {
                    merged[key] = value is null ? null : JsonValue.Create(Text(value));
                    customised = true;
                }
                else warnings.Add($"{key} must be text of 1–{MaxText} characters; ignored.");
                continue;
            }
            if (!Sections.TryGetValue(key, out var fields))
            {
                warnings.Add($"unknown key \"{key}\"; ignored.");
                continue;
            }
            if (value is not JsonObject section)
            {
                warnings.Add($"{key} must be an object; ignored.");
                continue;
            }
            var target = merged[key]!.AsObject();
            foreach (var (name, item) in section)
            {
                if (!fields.TryGetValue(name, out var field))
                {
                    warnings.Add($"unknown key \"{key}.{name}\"; ignored.");
                    continue;
                }
                if (TryValue(field, item, out var clean))
                {
                    target[name] = clean;
                    customised = true;
                }
                else warnings.Add($"{key}.{name}: {Describe(field)}; ignored.");
            }
        }

        var theme = merged.Deserialize<ThemeDefinition>(Json)!;
        return new(theme, customised, warnings);
    }

    private static bool TryValue(Field field, JsonNode? node, out JsonNode? clean)
    {
        clean = null;
        if (node is null) return field.Kind == Kind.NullablePath;
        if (field.Kind == Kind.TextList)
        {
            if (node is not JsonArray items || items.Count > MaxLines || !items.All(i => i is not null && TryText(i, out _)))
                return false;
            clean = new JsonArray(items.Select(i => (JsonNode)JsonValue.Create(Text(i!))).ToArray());
            return true;
        }
        if (node is not JsonValue value || !value.TryGetValue<string>(out var raw)) return false;
        var s = raw.Trim();
        string? result = field.Kind switch
        {
            Kind.Color when HexColor().IsMatch(s) => s.ToLowerInvariant(),
            Kind.Gradient when s.Length <= 500 && GradientPattern().IsMatch(s) && !Forbidden(s) => s,
            Kind.Font when FontPattern().IsMatch(s) => s,
            Kind.Path or Kind.NullablePath when IsSafePath(s) => s,
            Kind.Text when TryText(node, out _) => s,
            Kind.Choice => field.Choices.FirstOrDefault(c => string.Equals(c, s, StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };
        if (result is null) return false;
        clean = JsonValue.Create(result);
        return true;
    }

    private static string Text(JsonNode node) => node.GetValue<string>().Trim();

    private static bool TryText(JsonNode node, out string text)
    {
        text = "";
        if (node is not JsonValue v || !v.TryGetValue<string>(out var raw)) return false;
        text = raw.Trim();
        return text.Length is > 0 and <= MaxText && !text.Any(char.IsControl);
    }

    // Gradients go straight into a CSS custom property: no url(), no way out of the declaration.
    private static bool Forbidden(string s) =>
        s.Contains("url", StringComparison.OrdinalIgnoreCase)
        || s.Contains("expression", StringComparison.OrdinalIgnoreCase)
        || s.Contains("image", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafePath(string s) =>
        s.Length is > 0 and <= 500 && !s.StartsWith("//") && !s.Contains("..") && SafePath().IsMatch(s);

    private static string Describe(Field field) => field.Kind switch
    {
        Kind.Color => "must be a #rgb or #rrggbb colour",
        Kind.Gradient => "must be a linear-gradient() or radial-gradient() of colours",
        Kind.Font => "must be a font list of letters, digits, spaces, quotes, commas and hyphens",
        Kind.Path or Kind.NullablePath => "must be a path on the web app (e.g. brand/logo.png) or an https URL",
        Kind.Text => $"must be text of 1–{MaxText} characters",
        Kind.TextList => $"must be a list of at most {MaxLines} texts of 1–{MaxText} characters",
        Kind.Choice => $"must be one of {string.Join(", ", field.Choices)}",
        _ => "is not valid",
    };

    // Opaque hex only: the meal-stub renderer (Skia) and email clients read nothing else as CSS does.
    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"^(?:linear|radial)-gradient\([#a-zA-Z0-9.,%\s()-]+\)$")]
    private static partial Regex GradientPattern();

    [GeneratedRegex(@"^[A-Za-z0-9 ,'""-]{1,200}$")]
    private static partial Regex FontPattern();

    [GeneratedRegex(@"^(?:https://[A-Za-z0-9._~\-/?=&%:]+|[A-Za-z0-9._~\-/?=&%]+)$")]
    private static partial Regex SafePath();
}
