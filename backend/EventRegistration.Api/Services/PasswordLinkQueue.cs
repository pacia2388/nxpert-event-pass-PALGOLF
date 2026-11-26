using EventRegistration.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

/// <summary>
/// Sends the set-password links owed to imported accounts. A pending link is a row marker
/// (User.PasswordLinkQueuedAt), not an in-memory item, so a restart never loses one: the poller
/// picks every due marker, sends the link and clears it.
/// </summary>
public class PasswordLinkQueue(IServiceScopeFactory scopes, ILogger<PasswordLinkQueue> log) : BackgroundService
{
    public const int PollSeconds = 30;
    // 200 ms between sends keeps provider bursts down; 100 links fit in one 30 s poll (20 s).
    private const int BatchSize = 100;
    // The poller and a test's explicit call must not send the same link twice at once.
    private readonly SemaphoreSlim _one = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(PollSeconds));
        do
        {
            try { await ProcessDueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "The password link queue failed; retrying on the next poll."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Sends every link that is due now. The tests call it instead of waiting for the poll.</summary>
    public async Task ProcessDueAsync(CancellationToken ct = default)
    {
        await _one.WaitAsync(ct);
        try
        {
            List<int> ids;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTimeOffset.UtcNow;
                ids = await db.Users.AsNoTracking()
                    .Where(u => u.PasswordLinkQueuedAt != null && u.PasswordLinkQueuedAt <= now &&
                        u.PasswordHash == "" && u.DeactivatedAt == null)
                    .OrderBy(u => u.PasswordLinkQueuedAt).Select(u => u.Id).Take(BatchSize).ToListAsync(ct);
            }
            foreach (var id in ids)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    using var userScope = scopes.CreateScope();
                    var db = userScope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var user = await db.Users.FindAsync([id], ct);
                    // Skipped once the account has a password or was deactivated meanwhile.
                    if (user is { DeactivatedAt: null, PasswordHash: "" })
                        await userScope.ServiceProvider.GetRequiredService<PasswordSetupService>().IssueAsync(user);
                    // After sending: a crash in between repeats the link instead of losing it.
                    if (user is not null)
                    {
                        user.PasswordLinkQueuedAt = null;
                        await db.SaveChangesAsync(ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "The password link for user {Id} could not be sent.", id);
                }
                await Task.Delay(200, ct);
            }
        }
        finally { _one.Release(); }
    }
}
