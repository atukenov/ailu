using Ailu.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

// Privacy policy: the partner's phone number lives only in the invitation, which works for 24 hours, and
// goes with it. Once an invitation has expired, accepted or not, nothing needs it any more (the couple
// keeps its own record), so the row and the number in it are deleted.
public sealed class InvitationCleanupJob(AiluDb db, TimeProvider clock, ILogger<InvitationCleanupJob> log)
{
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var deleted = await db.Invitations.Where(i => i.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        if (deleted > 0) log.LogInformation("Deleted {Count} expired invitations", deleted);
        return deleted;
    }
}

public sealed class InvitationCleanupScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<InvitationCleanupScheduler> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<InvitationCleanupJob>().RunOnceAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Invitation cleanup failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
