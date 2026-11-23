using MimeKit;

namespace EventRegistration.Api.Services;

/// <summary>
/// Emails kept for a later send (background email retry, late Viber failure) are saved as .eml files
/// here; the database stores only the file name (<c>EmailRetry.EmailFile</c>, <c>ViberMessage.EmailFile</c>).
/// Folder: <c>App:MailQueueDir</c>, default <c>mail-queue</c> in the API folder. It holds personal
/// data: never serve it, and keep it across deploys (no <c>robocopy /MIR</c>).
/// </summary>
public class EmailFileStore
{
    // After this no retry can need a file: Viber failure reports older than 24 h are ignored, and a
    // retry gives up after 5 tries 5 minutes apart.
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(26);

    private readonly string _dir;

    public EmailFileStore(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["App:MailQueueDir"];
        if (string.IsNullOrWhiteSpace(configured))
            _dir = Path.Combine(env.ContentRootPath, "mail-queue");
        else
            _dir = Path.GetFullPath(configured, env.ContentRootPath);
    }

    /// <summary>Writes the whole email (headers, HTML, inline images, attachments); returns its file name.</summary>
    public async Task<string> SaveAsync(MimeMessage message)
    {
        Directory.CreateDirectory(_dir);
        var name = $"{Guid.NewGuid():N}.eml";
        await message.WriteToAsync(Path.Combine(_dir, name));
        return name;
    }

    /// <summary>The saved email, or null when the name is not valid or the file is gone.</summary>
    public async Task<MimeMessage?> LoadAsync(string? name)
    {
        var path = PathOf(name);
        if (path is null || !File.Exists(path))
            return null;
        return await MimeMessage.LoadAsync(path);
    }

    public void Delete(string? name)
    {
        var path = PathOf(name);
        if (path is not null && File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// The full path of a stored file. Only a bare "<c>name.eml</c>" is accepted, so a value read
    /// from the database can never point outside the folder.
    /// </summary>
    public string? PathOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (Path.GetFileName(name) != name || !name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
            return null;
        return Path.Combine(_dir, name);
    }

    /// <summary>Deletes files older than <see cref="KeepFor"/> that no pending retry uses.</summary>
    public void DeleteOld(IReadOnlySet<string> inUse)
    {
        if (!Directory.Exists(_dir))
            return;
        var cutoff = DateTime.UtcNow - KeepFor;
        foreach (var path in Directory.GetFiles(_dir, "*.eml"))
        {
            if (inUse.Contains(Path.GetFileName(path)))
                continue;
            if (File.GetLastWriteTimeUtc(path) < cutoff)
                File.Delete(path);
        }
    }
}
