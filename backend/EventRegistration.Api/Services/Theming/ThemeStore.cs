namespace EventRegistration.Api.Services.Theming;

/// <param name="Source">"file" when theme.json was read, "default" when there is none.</param>
public sealed record ThemeSnapshot(ThemeDefinition Theme, string Source, bool Customised);

/// <summary>
/// The deployment's active theme, read from theme.json beside the API (<c>Theme:Path</c>). The
/// file is never published: a developer adds it on the server, and an edit applies on the next
/// request (re-read whenever its time stamp or size changes). No file means PALGOLF.
/// Static like <see cref="Branding"/>, so email templates can reach it.
/// </summary>
public static class ThemeStore
{
    public static readonly ThemeSnapshot Default = new(BuiltInThemes.PalGolf, "default", false);

    private static readonly object Gate = new();
    private static string? _path;
    private static (DateTime Written, long Length)? _stamp;
    private static ThemeSnapshot _current = Default;

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
                    return _current = Default;
                }
                var stamp = (info.LastWriteTimeUtc, info.Length);
                if (_stamp == stamp) return _current;
                string text;
                try
                {
                    text = File.ReadAllText(path);
                }
                catch (IOException ex)
                {
                    // Mid-write: keep what we had and try again on the next request.
                    Log?.Invoke($"theme.json could not be read ({ex.Message}).");
                    return _current;
                }
                var resolved = ThemeResolver.Resolve(text);
                foreach (var warning in resolved.Warnings) Log?.Invoke($"theme.json: {warning}");
                _stamp = stamp;
                return _current = new ThemeSnapshot(resolved.Theme, "file", resolved.Customised);
            }
        }
    }
}
