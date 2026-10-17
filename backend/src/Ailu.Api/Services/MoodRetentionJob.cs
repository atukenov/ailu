using Ailu.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

// Privacy doc: a mood is kept for 12 months, then only counted. Each day older than that becomes one
// more in its person's monthly count, and the day itself, note included, is deleted.
public sealed class MoodRetentionJob(AiluDb db, TimeProvider clock, ILogger<MoodRetentionJob> log)
{
    public const int KeepMonths = 12;
    private const int Batch = 500;

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var cutoff = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddMonths(-KeepMonths);
        var total = 0;
        while (true)
        {
            var due = await db.Moods.Where(m => m.Date < cutoff).OrderBy(m => m.Date).Take(Batch).ToListAsync(ct);
            if (due.Count == 0) break;

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            foreach (var group in due.GroupBy(m => (m.CoupleId, m.UserId, Month: new DateOnly(m.Date.Year, m.Date.Month, 1))))
            {
                var (coupleId, userId, month) = group.Key;
                var row = await db.MoodMonths.FindAsync([coupleId, userId, month], ct);
                if (row is null)
                {
                    row = new MoodMonth { CoupleId = coupleId, UserId = userId, Month = month };
                    db.MoodMonths.Add(row);
                }
                foreach (var m in group)
                {
                    switch (m.Mood)
                    {
                        case MoodLevel.Hard: row.Hard++; break;
                        case MoodLevel.Tired: row.Tired++; break;
                        case MoodLevel.Even: row.Even++; break;
                        case MoodLevel.Good: row.Good++; break;
                        case MoodLevel.Great: row.Great++; break;
                    }
                }
            }
            db.Moods.RemoveRange(due);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
            total += due.Count;
        }
        if (total > 0) log.LogInformation("Folded {Count} moods older than {Months} months into monthly counts", total, KeepMonths);
        return total;
    }
}

public sealed class MoodRetentionScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<MoodRetentionScheduler> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6), clock);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<MoodRetentionJob>().RunOnceAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Mood retention failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
