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

    // One subscription covers both partners. There is no payment yet, so every couple is on Free.
    public CouplePlan Plan { get; set; }

    // Bytes of photos the couple currently stores, counted against the plan's quota. Changed only by
    // an atomic UPDATE in the same transaction as the photo row, never recounted from storage.
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

public enum PushKind { MorningTask, EveningAnswer }

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
