namespace Ailu.Api.Data;

// The couple is the primary entity: every shared record is keyed by CoupleId, never by UserId.
// Authorship, where it matters, is a separate field.

public sealed class User
{
    public Guid Id { get; set; }
    public required string Phone { get; set; }
    public string? Name { get; set; }
    public string Locale { get; set; } = "ru";
    public string TimeZone { get; set; } = "Asia/Almaty";
    public DateTimeOffset CreatedAt { get; set; }

    // Mood is processed on explicit consent only (privacy doc). Null means not given or withdrawn.
    public DateTimeOffset? MoodConsentAt { get; set; }
}

public sealed class PhoneCode
{
    public Guid Id { get; set; }
    public required string Phone { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public enum CoupleStatus { Active, Paused, Ended }

public enum CouplePlan { Free, Plus, Premium }

public sealed class Couple
{
    public Guid Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public CoupleStatus Status { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    // The couple's calendar day decides "today" for tasks, questions and the streak.
    public string TimeZone { get; set; } = "Asia/Almaty";

    // Both are caches derived from NurLedger, updated in the same transaction as the ledger row.
    // Earned only ever grows and drives the phase; Balance can be spent (e.g. streak restores).
    public int NurEarned { get; set; }
    public int NurBalance { get; set; }

    public int StreakDays { get; set; }
    public DateOnly? StreakLastDay { get; set; }
    public DateOnly? StreakForgivenOn { get; set; }

    // The streak that broke most recently, kept so it can still be restored after starting over.
    public int StreakLostDays { get; set; }
    public DateOnly? StreakLostLastDay { get; set; }
    public DateOnly? StreakRestartedOn { get; set; }

    // One subscription covers both partners. There is no payment yet, so every couple is on Free.
    public CouplePlan Plan { get; set; }

    // Photos and bytes the couple currently stores, counted against the plan's limits. Changed only by
    // an atomic UPDATE in the same transaction as the photo row, never recounted from storage.
    public int PhotoCount { get; set; }
    public long StorageBytes { get; set; }

    public List<CoupleMember> Members { get; set; } = [];
}

public enum MemberRole { A, B }

public sealed class CoupleMember
{
    public Guid CoupleId { get; set; }
    public Guid UserId { get; set; }
    public MemberRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }

    public Couple? Couple { get; set; }
    public User? User { get; set; }
}

public sealed class Invitation
{
    public Guid Id { get; set; }
    public Guid InviterUserId { get; set; }
    public required string Phone { get; set; }
    public required string Code { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? CoupleId { get; set; }
}

// Content library. Tasks and questions come from the server, not the app bundle.
public sealed class DailyTask
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Category { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public int CostKzt { get; set; }
    public int DurationMin { get; set; }
}

public sealed class Question
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Text { get; set; }
}

public sealed class CoupleTask
{
    public Guid Id { get; set; }
    public Guid CoupleId { get; set; }
    public int DailyTaskId { get; set; }
    public DateOnly Date { get; set; }
    // 0 is the task of the day; paid couples can ask for more once the previous one is done.
    public int Ordinal { get; set; }
    public DateTimeOffset? AcceptedAtA { get; set; }
    public DateTimeOffset? AcceptedAtB { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }

    public DailyTask? DailyTask { get; set; }
}

public sealed class DailyQuestion
{
    public Guid Id { get; set; }
    public Guid CoupleId { get; set; }
    public int QuestionId { get; set; }
    public DateOnly Date { get; set; }
    public string? AnswerA { get; set; }
    public DateTimeOffset? AnsweredAtA { get; set; }
    public string? AnswerB { get; set; }
    public DateTimeOffset? AnsweredAtB { get; set; }
    public DateTimeOffset? RevealedAt { get; set; }

    public Question? Question { get; set; }
}

// One row per partner per day on which they did something shared. A streak day needs both.
public sealed class CoupleActivity
{
    public Guid CoupleId { get; set; }
    public DateOnly Date { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum NurSource { Task, Question, Memory, Date, GoalStep, Achievement, Mood, LoveQuiz, StreakRestore }

// Append-only. The source of truth for every нұр the couple has; nothing updates or deletes rows.
// Awards are positive; spending (a streak restore) is a negative row that lowers the balance only.
public sealed class NurLedgerEntry
{
    public long Id { get; set; }
    public Guid CoupleId { get; set; }
    public NurSource SourceType { get; set; }
    public required string SourceId { get; set; }
    public int Amount { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// An Expo push token for one of a user's devices. A token moves to whoever signs in on that device last.
public sealed class Device
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string PushToken { get; set; }
    public required string Platform { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public enum PushKind { MorningTask, EveningAnswer, GoalReminder }

// One row per push actually sent. The unique (UserId, Date, Kind) key is what keeps each person to
// at most one morning and one evening push a day, even across restarts or several API instances.
public sealed class PushLog
{
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public PushKind Kind { get; set; }
    public Guid CoupleId { get; set; }
    public DateTimeOffset SentAt { get; set; }
}

// A shared memory. Each one has an author: only the author can change or delete it, both partners see it.
public sealed class Memory
{
    public Guid Id { get; set; }
    public Guid CoupleId { get; set; }
    public Guid AuthorId { get; set; }
    public required string Title { get; set; }
    public string? Text { get; set; }
    public string? Place { get; set; }
    public DateOnly HappenedOn { get; set; }
    public Guid? CoupleTaskId { get; set; }
    public bool Favorite { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Deleted memories disappear at once and free their quota; the files go after 30 days in the bin.
    public DateTimeOffset? DeletedAt { get; set; }

    public List<MemoryPhoto> Photos { get; set; } = [];
    public List<MemoryReaction> Reactions { get; set; } = [];
    public User? Author { get; set; }
}

public sealed class MemoryPhoto
{
    public Guid Id { get; set; }
    public Guid MemoryId { get; set; }
    public Guid CoupleId { get; set; }
    public int Position { get; set; }
    public long Bytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// A partner's heart on a memory. The first one from the partner who didn't write it earns the couple нұр.
public sealed class MemoryReaction
{
    public Guid MemoryId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum MoodLevel { Hard, Tired, Even, Good, Great }

// One person's mood for one of the couple's days. The partner sees the mood and when it was marked;
// the note stays with the author until they share it.
public sealed class MoodEntry
{
    public Guid CoupleId { get; set; }
    public DateOnly Date { get; set; }
    public Guid UserId { get; set; }
    public MoodLevel Mood { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? NoteSharedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// Moods older than 12 months survive only as counts per person per month.
public sealed class MoodMonth
{
    public Guid CoupleId { get; set; }
    public Guid UserId { get; set; }
    public DateOnly Month { get; set; }
    public int Hard { get; set; }
    public int Tired { get; set; }
    public int Even { get; set; }
    public int Good { get; set; }
    public int Great { get; set; }
}

public enum GoalCategory { Travel, Home, Health, Money, Together }

// A shared goal. One partner proposes it and the other joins; from then on it belongs to both:
// either can edit it, steps close only when both have marked them, and removing it takes both.
public sealed class Goal
{
    public Guid Id { get; set; }
    public Guid CoupleId { get; set; }
    public Guid CreatedById { get; set; }
    public required string Title { get; set; }
    public GoalCategory Category { get; set; }

    // The first day of the month the couple aims for, or none.
    public DateOnly? TargetMonth { get; set; }
    public bool WeeklyReminder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? JoinedAt { get; set; }

    // Set when the last step closes, cleared if a new step is added.
    public DateTimeOffset? CompletedAt { get; set; }

    public Guid? RemovalRequestedById { get; set; }
    public DateTimeOffset? RemovalRequestedAt { get; set; }

    // Goals are never deleted: a removed goal is only hidden, so its нұр keeps a source.
    public DateTimeOffset? RemovedAt { get; set; }

    public List<GoalStep> Steps { get; set; } = [];
}

// One partner marks a step done and the other confirms, like the task of the day.
public sealed class GoalStep
{
    public Guid Id { get; set; }
    public Guid GoalId { get; set; }
    public Guid CoupleId { get; set; }
    public int Position { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? MarkedById { get; set; }
    public DateTimeOffset? MarkedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}
