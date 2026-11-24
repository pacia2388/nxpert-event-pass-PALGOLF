namespace EventRegistration.Api.Services.Theming;

/// <param name="Source">"file" when the theme file was read, "default" when there is none.</param>
public sealed record ThemeSnapshot(ThemeDefinition Theme, string Source, bool Customised);

/// <summary>
/// The deployment's active theme, read from the file the <c>theme</c> setting names beside the API
/// (theme.palgolf.json or theme.nxpass.json). An edit applies on the next request (re-read
/// whenever its time stamp or size changes). No file means PALGOLF.
/// Static like <see cref="Branding"/>, so email templates can reach it.
/// </summary>
public static class ThemeStore
{
    public static readonly ThemeSnapshot Default = new(BuiltInThemes.PalGolf, "default", false);

    private static readonly object Gate = new();
    private static string? _path;
    private static (DateTime Written, long Length)? _stamp;
    private static ThemeSnapshot _current = Default;
    private static string? _text;

    /// <summary>The web app's public URL (App:FrontendBaseUrl): logo paths are relative to it.</summary>
    public static string WebBaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>An absolute URL for a theme logo path, for emails.</summary>
    public static string LogoUrl(string path) =>
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? path : $"{WebBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    /// <summary>Where problems with the file are reported (set at startup to the app's logger).</summary>
    public static Action<string>? Log { get; set; }

    public static string? FilePath
    {
        get => _path;
        set
        {
            lock (Gate)
            {
                _path = value;
                _stamp = null;
                _text = null;
                _current = Default;
            }
        }
    }

    public static ThemeSnapshot Current
    {
        get
        {
            var path = _path;
            if (path is null) return Default;
            var info = new FileInfo(path);
            lock (Gate)
            {
                if (!info.Exists)
                {
                    _stamp = null;
                    _text = null;
                    return _current = Default;
                }
                var stamp = (info.LastWriteTimeUtc, info.Length);
                // File times tick coarsely: a same-size rewrite within a tick keeps its stamp, so a
                // file written in the last few seconds is read again until it settles.
                // A time stamp in the future (a file copied from a machine whose clock runs ahead)
                // counts as settled too, or the file would be read on every request.
                var settled = (DateTime.UtcNow - info.LastWriteTimeUtc).Duration() > TimeSpan.FromSeconds(2);
                if (_stamp == stamp && settled) return _current;
                string text;
                try
                {
                    text = File.ReadAllText(path);
                }
                catch (IOException ex)
                {
                    // Mid-write: keep what we had and try again on the next request.
                    Report($"{Path.GetFileName(path)} could not be read ({ex.Message}).");
                    return _current;
                }
                if (_stamp == stamp && text == _text) return _current;
                _text = text;
                var resolved = ThemeResolver.Resolve(text);
                foreach (var warning in resolved.Warnings) Report($"{Path.GetFileName(path)}: {warning}");
                _stamp = stamp;
                return _current = new ThemeSnapshot(resolved.Theme, "file", resolved.Customised);
            }
        }
    }

    // A theme problem is only ever reported, never allowed to fail the request that found it
    // (the logger may belong to a host that has shut down).
    private static void Report(string message)
    {
        try
        {
            Log?.Invoke(message);
        }
        catch (Exception)
        {
            // Nothing else to do: the theme already fell back.
        }
    }
}
