using Ailu.Api.Data;
using Ailu.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ailu.Api.Services;

// Store reviewers can't receive our SMS and have no partner, so a fixed number (Auth:ReviewPhone)
// signs in with a fixed code (Auth:ReviewCode) to a ready-made couple. The partner has no phone, so
// nobody can sign in as her; she "plays along" each time the reviewer opens Today: accepts the task,
// confirms it once the reviewer marks it done, answers the question and marks a mood.
// Nothing here runs unless both settings are configured.
public sealed class DemoCouple(AiluDb db, DailyContentService content, ActivityService activity, NurLedgerService nur,
    TimeProvider clock)
{
    private const int HistoryDays = 12;

    private static readonly string[] Answers =
    [
        "Когда ты смеёшься над своими же шутками раньше, чем договоришь.",
        "Наша первая поездка на Медеу, когда мы опоздали на последний автобус.",
        "Хочу, чтобы мы чаще гуляли вечером без телефонов."
    ];

    // Called after the reviewer signs in. Gives them a name and, if they have no couple (first sign-in,
    // or they ended it or deleted the account while testing), a fresh demo couple with some history.
    public async Task EnsureAsync(User reviewer, CancellationToken ct = default)
    {
        reviewer.Name ??= "Арман";
        var inCouple = await db.CoupleMembers.AnyAsync(m => m.UserId == reviewer.Id && m.LeftAt == null
            && m.Couple!.Status != CoupleStatus.Ended, ct);
        if (inCouple)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var now = clock.GetUtcNow();
        var partner = new User { Id = Guid.NewGuid(), Name = "Айгерим", CreatedAt = now, MoodConsentAt = now };
        var couple = new Couple
        {
            Id = Guid.NewGuid(), Status = CoupleStatus.Active, IsDemo = true, TimeZone = reviewer.TimeZone,
            StartedAt = now.AddDays(-HistoryDays - 11),
            Members =
            [
                new CoupleMember { UserId = partner.Id, Role = MemberRole.A, JoinedAt = now },
                new CoupleMember { UserId = reviewer.Id, Role = MemberRole.B, JoinedAt = now }
            ]
        };
        db.Users.Add(partner);
        db.Couples.Add(couple);
        await db.SaveChangesAsync(ct);

        // The last days done together: task and question each day, so the moon and streak have grown.
        var today = CoupleCalendar.Today(couple, clock);
        var tasks = await db.TaskLibrary.Select(t => t.Id).ToListAsync(ct);
        var questions = await db.Questions.Select(q => q.Id).ToListAsync(ct);
        var usedTasks = new List<int>();
        var usedQuestions = new List<int>();
        for (var i = HistoryDays; i >= 1; i--)
        {
            var day = today.AddDays(-i);
            var at = now.AddDays(-i);
            var task = new CoupleTask
            {
                Id = Guid.NewGuid(), CoupleId = couple.Id, Date = day,
                DailyTaskId = DailyContentService.PickLeastUsed(tasks, usedTasks, couple.Id, day),
                AcceptedAtA = at, AcceptedAtB = at, CompletedAt = at, CompletedByUserId = reviewer.Id, ConfirmedAt = at
            };
            var question = new DailyQuestion
            {
                Id = Guid.NewGuid(), CoupleId = couple.Id, Date = day,
                QuestionId = DailyContentService.PickLeastUsed(questions, usedQuestions, couple.Id, day),
                AnswerA = Answers[i % Answers.Length], AnsweredAtA = at,
                AnswerB = Answers[(i + 1) % Answers.Length], AnsweredAtB = at, RevealedAt = at
            };
            usedTasks.Add(task.DailyTaskId);
            usedQuestions.Add(question.QuestionId);
            db.CoupleTasks.Add(task);
            db.DailyQuestions.Add(question);
            foreach (var who in new[] { partner.Id, reviewer.Id })
                db.CoupleActivities.Add(new CoupleActivity { CoupleId = couple.Id, Date = day, UserId = who, CreatedAt = at });
            await nur.AwardAsync(couple, NurSource.Task, task.Id.ToString("N"), Nur.TaskCompleted, day, ct);
            await nur.AwardAsync(couple, NurSource.Question, question.Id.ToString("N"), Nur.QuestionAnsweredByBoth, day, ct);
        }
        couple.StreakDays = HistoryDays;
        couple.StreakLastDay = today.AddDays(-1);

        db.Memories.Add(new Memory
        {
            Id = Guid.NewGuid(), CoupleId = couple.Id, AuthorId = partner.Id, Title = "Ужин на крыше",
            Text = "Первый тёплый вечер, и мы досидели до закрытия.", Place = "Алматы",
            HappenedOn = today.AddDays(-5), CreatedAt = now.AddDays(-5)
        });
        db.Memories.Add(new Memory
        {
            Id = Guid.NewGuid(), CoupleId = couple.Id, AuthorId = reviewer.Id, Title = "Прогулка у Большого Алматинского озера",
            Place = "БАО", HappenedOn = today.AddDays(-9), CreatedAt = now.AddDays(-9), Favorite = true
        });

        var goalId = Guid.NewGuid();
        db.Goals.Add(new Goal
        {
            Id = goalId, CoupleId = couple.Id, CreatedById = partner.Id, Title = "Поездка в Турцию",
            Category = GoalCategory.Travel, TargetMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(6),
            WeeklyReminder = true, CreatedAt = now.AddDays(-8), JoinedAt = now.AddDays(-8),
            Steps =
            [
                new GoalStep { Id = Guid.NewGuid(), GoalId = goalId, CoupleId = couple.Id, Position = 0, Title = "Выбрать даты",
                    CreatedAt = now.AddDays(-8), MarkedById = partner.Id, MarkedAt = now.AddDays(-6), ClosedAt = now.AddDays(-6) },
                new GoalStep { Id = Guid.NewGuid(), GoalId = goalId, CoupleId = couple.Id, Position = 1, Title = "Откладывать по 20 000 ₸ в месяц",
                    CreatedAt = now.AddDays(-8) },
                new GoalStep { Id = Guid.NewGuid(), GoalId = goalId, CoupleId = couple.Id, Position = 2, Title = "Купить билеты",
                    CreatedAt = now.AddDays(-8) }
            ]
        });

        // One achievement already theirs, and one the partner marked for the reviewer to confirm.
        var achievements = await db.Achievements.Where(a => a.Slug == "first-coffee" || a.Slug == "sunrise")
            .ToDictionaryAsync(a => a.Slug, ct);
        if (achievements.TryGetValue("first-coffee", out var coffee))
        {
            db.CoupleAchievements.Add(new CoupleAchievement
            {
                CoupleId = couple.Id, AchievementId = coffee.Id, MarkedById = reviewer.Id, MarkedAt = now.AddDays(-10),
                HappenedOn = today.AddDays(-10), EarnedAt = now.AddDays(-10)
            });
            await nur.AwardAsync(couple, NurSource.Achievement, coffee.Slug, Nur.AchievementEarned, today.AddDays(-10), ct);
        }
        if (achievements.TryGetValue("sunrise", out var sunrise))
            db.CoupleAchievements.Add(new CoupleAchievement
            {
                CoupleId = couple.Id, AchievementId = sunrise.Id, MarkedById = partner.Id, MarkedAt = now.AddDays(-2),
                HappenedOn = today.AddDays(-2)
            });

        await db.SaveChangesAsync(ct);
    }

    // The partner's moves for today. Each step happens once; calling this again changes nothing.
    public async Task PlayPartnerAsync(CoupleScope scope, CancellationToken ct = default)
    {
        if (!scope.Couple.IsDemo || scope.Partner is null) return;
        var couple = scope.Couple;
        var partnerId = scope.Partner.UserId;
        var partnerIsA = scope.Partner.Role == MemberRole.A;
        var today = CoupleCalendar.Today(couple, clock);
        var now = clock.GetUtcNow();
        var (task, question) = await content.EnsureAsync(couple, today, ct);

        if (partnerIsA) task.AcceptedAtA ??= now;
        else task.AcceptedAtB ??= now;
        if (task.CompletedAt is not null && task.ConfirmedAt is null && task.CompletedByUserId != partnerId)
        {
            task.ConfirmedAt = now;
            await nur.AwardAsync(couple, NurSource.Task, task.Id.ToString("N"), Nur.TaskCompleted, today, ct);
        }

        if (question.RevealedAt is null)
        {
            var answer = Answers[today.DayNumber % Answers.Length];
            if (partnerIsA && question.AnswerA is null) { question.AnswerA = answer; question.AnsweredAtA = now; }
            if (!partnerIsA && question.AnswerB is null) { question.AnswerB = answer; question.AnsweredAtB = now; }
            if (question.AnswerA is not null && question.AnswerB is not null)
            {
                question.RevealedAt = now;
                await nur.AwardAsync(couple, NurSource.Question, question.Id.ToString("N"), Nur.QuestionAnsweredByBoth, today, ct);
            }
        }

        if (!await db.Moods.AnyAsync(m => m.CoupleId == couple.Id && m.Date == today && m.UserId == partnerId, ct))
            db.Moods.Add(new MoodEntry
            {
                CoupleId = couple.Id, Date = today, UserId = partnerId, Mood = MoodLevel.Good, CreatedAt = now, UpdatedAt = now
            });

        // Achievements the reviewer marked since: she remembers them too.
        var marked = await db.CoupleAchievements.Include(x => x.Achievement)
            .Where(x => x.CoupleId == couple.Id && x.MarkedById != partnerId && x.EarnedAt == null).ToListAsync(ct);
        foreach (var mark in marked)
        {
            mark.EarnedAt = now;
            await nur.AwardAsync(couple, NurSource.Achievement, mark.Achievement!.Slug, Nur.AchievementEarned, today, ct);
        }

        await activity.RecordAsync(couple, partnerId, today, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two screens asked for Today at once and the other request made the same moves first.
            db.ChangeTracker.Clear();
        }
    }
}
