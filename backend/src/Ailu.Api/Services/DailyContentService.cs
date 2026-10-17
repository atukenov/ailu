using Ailu.Api.Data;
using Ailu.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ailu.Api.Services;

// Hands each couple one task and one question per calendar day. Items are created lazily on the
// first read of the day; the morning worker will later create them ahead of the 09:00 push.
public sealed class DailyContentService(AiluDb db)
{
    public async Task<(CoupleTask Task, DailyQuestion Question)> EnsureAsync(Couple couple, DateOnly day,
        CancellationToken ct = default, bool retried = false)
    {
        var task = await FindTaskAsync(couple.Id, day, ct);
        var question = await FindQuestionAsync(couple.Id, day, ct);
        if (task is not null && question is not null) return (task, question);

        if (task is null)
        {
            var used = await db.CoupleTasks.Where(t => t.CoupleId == couple.Id).Select(t => t.DailyTaskId).ToListAsync(ct);
            var library = await db.DailyTasks.Select(t => t.Id).ToListAsync(ct);
            db.CoupleTasks.Add(new CoupleTask
            {
                Id = Guid.NewGuid(), CoupleId = couple.Id, Date = day,
                DailyTaskId = PickLeastUsed(library, used, couple.Id, day, await PreferredTasksAsync(couple.Id, ct))
            });
        }

        if (question is null)
        {
            var used = await db.DailyQuestions.Where(q => q.CoupleId == couple.Id).Select(q => q.QuestionId).ToListAsync(ct);
            var library = await db.Questions.Select(q => q.Id).ToListAsync(ct);
            db.DailyQuestions.Add(new DailyQuestion
            {
                Id = Guid.NewGuid(), CoupleId = couple.Id, Date = day,
                QuestionId = PickLeastUsed(library, used, couple.Id, day)
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (!retried && e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The partner opened the app at the same moment and created today's items first.
            foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            return await EnsureAsync(couple, day, ct, retried: true);
        }

        return ((await FindTaskAsync(couple.Id, day, ct))!, (await FindQuestionAsync(couple.Id, day, ct))!);
    }

    // One more task for today, never one the couple already has today. The caller holds the couple
    // row lock, so ordinals don't collide.
    public async Task<CoupleTask> AddExtraAsync(Couple couple, DateOnly day, int ordinal, CancellationToken ct = default)
    {
        var used = await db.CoupleTasks.Where(t => t.CoupleId == couple.Id)
            .Select(t => new { t.DailyTaskId, t.Date }).ToListAsync(ct);
        var todays = used.Where(t => t.Date == day).Select(t => t.DailyTaskId).ToHashSet();
        var library = await db.DailyTasks.Select(t => t.Id).ToListAsync(ct);
        var fresh = library.Where(id => !todays.Contains(id)).ToList();

        var task = new CoupleTask
        {
            Id = Guid.NewGuid(), CoupleId = couple.Id, Date = day, Ordinal = ordinal,
            DailyTaskId = PickLeastUsed(fresh.Count > 0 ? fresh : library, used.Select(t => t.DailyTaskId), couple.Id, day,
                await PreferredTasksAsync(couple.Id, ct))
        };
        db.CoupleTasks.Add(task);
        await db.SaveChangesAsync(ct);
        await db.Entry(task).Reference(t => t.DailyTask).LoadAsync(ct);
        return task;
    }

    // Prefer items the couple has seen least, and among those pick a stable one for the day,
    // so the library is worked through before anything repeats. Preferred items (tasks that suit the
    // couple's love languages) count each use as half, so they come up about twice as often, and go
    // first among items seen equally often.
    public static int PickLeastUsed(IReadOnlyCollection<int> library, IEnumerable<int> used, Guid coupleId, DateOnly day,
        IReadOnlySet<int>? preferred = null)
    {
        if (library.Count == 0) throw new InvalidOperationException("The content library is empty.");
        var counts = used.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        int Used(int id) => counts.GetValueOrDefault(id);
        int Weight(int id) => preferred?.Contains(id) == true ? Used(id) : 2 * Used(id);
        var lightest = library.Min(Weight);
        var pool = library.Where(id => Weight(id) == lightest).ToList();
        var fewest = pool.Min(Used);
        pool = pool.Where(id => Used(id) == fewest).ToList();
        if (pool.Any(id => preferred?.Contains(id) == true)) pool = pool.Where(id => preferred!.Contains(id)).ToList();
        var candidates = pool.Order().ToArray();
        var seed = (uint)(BitConverter.ToInt32(coupleId.ToByteArray()) ^ day.DayNumber);
        return candidates[seed % candidates.Length];
    }

    // Tasks in the categories that suit the languages each partner leans to in the couple's latest
    // love-languages result. Empty until the couple has one.
    private async Task<IReadOnlySet<int>> PreferredTasksAsync(Guid coupleId, CancellationToken ct)
    {
        var picks = await db.LoveQuizRounds.Where(r => r.CoupleId == coupleId && r.CompletedAt != null)
            .OrderByDescending(r => r.CompletedAt).Take(1)
            .SelectMany(r => r.Answers.Select(a => a.Picks)).ToListAsync(ct);
        if (picks.Count == 0) return new HashSet<int>();
        var categories = picks.SelectMany(p => LoveQuiz.Top(LoveQuiz.Scores(p))).SelectMany(LoveQuiz.TaskCategories)
            .Distinct().ToList();
        return (await db.DailyTasks.Where(t => categories.Contains(t.Category)).Select(t => t.Id).ToListAsync(ct)).ToHashSet();
    }

    private Task<CoupleTask?> FindTaskAsync(Guid coupleId, DateOnly day, CancellationToken ct) =>
        db.CoupleTasks.Include(t => t.DailyTask)
            .SingleOrDefaultAsync(t => t.CoupleId == coupleId && t.Date == day && t.Ordinal == 0, ct);

    private Task<DailyQuestion?> FindQuestionAsync(Guid coupleId, DateOnly day, CancellationToken ct) =>
        db.DailyQuestions.Include(q => q.Question).SingleOrDefaultAsync(q => q.CoupleId == coupleId && q.Date == day, ct);
}
