namespace Ailu.Api.Data;

// The couple is the primary entity: every shared record is keyed by CoupleId, never by UserId.
// Authorship, where it matters, is a separate field.

public sealed class User
{
    public Guid Id { get; set; }

    // Null only on a deleted account, which keeps its row so shared history still has an author id.
    public string? Phone { get; set; }
    public string? Name { get; set; }
    public string Locale { get; set; } = "ru";
    public string TimeZone { get; set; } = "Asia/Almaty";
    public DateTimeOffset CreatedAt { get; set; }

    // Mood is processed on explicit consent only (privacy doc). Null means not given or withdrawn.
    public DateTimeOffset? MoodConsentAt { get; set; }

    // Set when the person deletes their account. The row is then only an anonymous id: no phone, no name.
    public DateTimeOffset? DeletedAt { get; set; }
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

    // One subscription covers both partners. A cache of the best live row in Subscriptions, set by
    // BillingService whenever a subscription changes and by the expiry job when one runs out.
    public CouplePlan Plan { get; set; }

    // Photos and bytes the couple currently stores, counted against the plan's limits. Changed only by
    // an atomic UPDATE in the same transaction as the photo row, never recounted from storage.
    public int PhotoCount { get; set; }
    public long StorageBytes { get; set; }

    // The ready-made couple store reviewers sign in to (Auth:ReviewPhone). Its partner plays along.
    public bool IsDemo { get; set; }

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

// Content library. Tasks and questions come from the server, not the app bundle. Every item is
// written in Russian and Kazakh; each partner reads it in their own language (English reads Russian).
// An item added to the library without its Kazakh text yet shows the Russian one.
public sealed class DailyTask
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Category { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public required string TitleKk { get; set; }
    public required string BodyKk { get; set; }
    public int CostKzt { get; set; }
    public int DurationMin { get; set; }

    // Set on a personal task written by AI for one Premium couple. Such a task belongs to that couple
    // only and is never handed to anyone else; the library is the rows where this is null.
    public Guid? CoupleId { get; set; }

    public string TitleIn(string? locale) => Locales.Pick(locale, Title, TitleKk);
    public string BodyIn(string? locale) => Locales.Pick(locale, Body, BodyKk);
}

public sealed class Question
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Text { get; set; }
    public required string TextKk { get; set; }

    public string TextIn(string? locale) => Locales.Pick(locale, Text, TextKk);
}

public static class Locales
{
    public const string Kazakh = "kk";

    public static string Pick(string? locale, string ru, string kk) =>
        locale == Kazakh && !string.IsNullOrWhiteSpace(kk) ? kk : ru;
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

    // When the full and screen sizes moved to the archive storage class (a year after upload).
    public DateTimeOffset? ArchivedAt { get; set; }
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

// One taking of the love-languages quiz by the couple. It completes when both partners have answered;
// until then neither sees the other's picks. Taking it again starts a new round, and the latest
// completed one is the couple's result.
public sealed class LoveQuizRound
{
    public Guid Id { get; set; }
    public Guid CoupleId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    // What completing it earned: 0 when the couple already had the quiz's нұр this quarter.
    public int NurAwarded { get; set; }

    public List<LoveQuizAnswer> Answers { get; set; } = [];
}

// One partner's picks for one round: a language code per question, in question order.
public sealed class LoveQuizAnswer
{
    public Guid RoundId { get; set; }
    public Guid UserId { get; set; }
    public required string[] Picks { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
}

public enum AchievementCategory { Firsts, Events, Travel }

// The achievement library. Like tasks and questions it is server content, not part of the app build, so
// a new set (say, for Наурыз) ships without a release. Texts come in Russian and Kazakh, like the tasks.
public sealed class Achievement
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public AchievementCategory Category { get; set; }

    // The plan that opens it: Free has the basic set, Plus and Premium all of them (monetization doc).
    public CouplePlan Tier { get; set; }
    public int Position { get; set; }
    public required string Icon { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public required string TitleKk { get; set; }
    public required string BodyKk { get; set; }

    public string TitleIn(string? locale) => Locales.Pick(locale, Title, TitleKk);
    public string BodyIn(string? locale) => Locales.Pick(locale, Body, BodyKk);
}

// A couple's achievement. Achievements are for things that happened in real life (gamification doc), so
// one partner says "we did it" and the other confirms, like the task of the day. Until then the mark can
// be taken back; once earned it is permanent: achievements, like the phase, are never lost.
public sealed class CoupleAchievement
{
    public Guid CoupleId { get; set; }
    public int AchievementId { get; set; }
    public Guid MarkedById { get; set; }
    public DateTimeOffset MarkedAt { get; set; }

    // The couple's day it happened, which the card shows. Defaults to the day it was marked.
    public DateOnly HappenedOn { get; set; }
    public Guid? MemoryId { get; set; }
    public DateTimeOffset? EarnedAt { get; set; }

    public Achievement? Achievement { get; set; }
}

// Gift and Promo are plans given by a code (PlanCode), not bought in a store.
public enum StoreProvider { Apple, Google, Trial, Gift, Promo }

public enum SubscriptionStatus { Active, Grace, Pending, Expired, Revoked, Replaced }

// A store subscription (or the free trial, or a redeemed code) that pays for a couple. One partner pays,
// both get the plan. The store is the source of truth: rows are only ever written from what Apple or
// Google return to the server, never from what the app says. StoreId is Apple's original transaction id
// or Google's purchase token, so renewals update the same row; for a code it is the redemption's id.
public sealed class Subscription
{
    public Guid Id { get; set; }
    public Guid CoupleId { get; set; }
    public Guid PayerUserId { get; set; }
    public StoreProvider Provider { get; set; }
    public required string StoreId { get; set; }
    public required string ProductId { get; set; }
    public CouplePlan Tier { get; set; }
    public SubscriptionStatus Status { get; set; }

    // The end of the paid period, or of the grace period while the store retries a failed payment.
    public DateTimeOffset PeriodEnd { get; set; }
    public bool AutoRenew { get; set; }
    public bool Sandbox { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool Entitles(DateTimeOffset now) =>
        Status is SubscriptionStatus.Active or SubscriptionStatus.Grace && PeriodEnd > now;
}

public enum YearStoryStatus { Queued, Ready, Failed }

// A paid "video story of the year" (roadmap phase 4): a one-minute film made from the couple's best memories
// and dates of one calendar year. One per couple per year. Like a subscription, a row is only written from
// what the store says about the purchase. The storyboard is fixed when it is ordered, so the film shows what
// the couple saw in the preview they paid for.
public sealed class YearStory
{
    public Guid Id { get; set; }
    public Guid CoupleId { get; set; }
    public int Year { get; set; }
    public Guid PayerUserId { get; set; }
    public StoreProvider Provider { get; set; }

    // Apple's transaction id or Google's purchase token.
    public required string StoreId { get; set; }
    public required string ProductId { get; set; }
    public bool Sandbox { get; set; }

    // The payer's language at the time of the order: the film's captions are written in it.
    public string Locale { get; set; } = "ru";

    // The slides as JSON (YearStoryPlan.Slides), frozen at the order.
    public required string Storyboard { get; set; }
    public YearStoryStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? Error { get; set; }
    public long VideoBytes { get; set; }
    public DateTimeOffset OrderedAt { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
}

public enum AiPickKind { Date, Home, Film, Music }

// The budget a couple sets for a date or an evening at home: free, up to about 5 000 ₸, or anything.
public enum AiBudget { Any, Free, Low }

public enum AiPickStatus { Pending, Ready, Failed }

// One AI selection ("подборка") a Premium partner asked for: five ideas of one kind. Both partners see it.
// The row is written before the model is asked, so the monthly limit holds when both ask at once; a
// failed one gives its place back.
public sealed class AiPick
{
    public Guid Id { get; set; }
    public Guid CoupleId { get; set; }
    public Guid RequestedById { get; set; }
    public AiPickKind Kind { get; set; }
    public AiBudget Budget { get; set; }

    // What the partner typed for this selection ("что-то уютное, без кино"). It goes to the model as is.
    public string? Note { get; set; }
    public AiPickStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // The first day of the couple's calendar month it counts against.
    public DateOnly Month { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public List<AiPickItem> Items { get; set; } = [];
}

// One idea in a selection, in Russian and Kazakh like the task library. Either partner can save it.
public sealed class AiPickItem
{
    public Guid Id { get; set; }
    public Guid PickId { get; set; }
    public Guid CoupleId { get; set; }
    public int Position { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public required string TitleKk { get; set; }
    public required string BodyKk { get; set; }
    public int CostKzt { get; set; }
    public int DurationMin { get; set; }
    public DateTimeOffset? SavedAt { get; set; }
    public Guid? SavedById { get; set; }

    public string TitleIn(string? locale) => Locales.Pick(locale, Title, TitleKk);
    public string BodyIn(string? locale) => Locales.Pick(locale, Body, BodyKk);
}

public enum PlanCodeKind { Promo, Gift }

// A code that gives the couple who enters it a plan for a while. A promo code is made by the team for many
// couples (each couple once); a gift code is bought by someone for one couple and works once.
public sealed class PlanCode
{
    public Guid Id { get; set; }

    // Stored normalized: upper case, no spaces or dashes.
    public required string Code { get; set; }
    public PlanCodeKind Kind { get; set; }
    public CouplePlan Tier { get; set; }

    // How long the plan lasts from the day it is entered.
    public int Months { get; set; }
    public int Days { get; set; }

    // Null: as many couples as like. A gift is always 1.
    public int? MaxRedemptions { get; set; }
    public int Redemptions { get; set; }

    // A code can't be entered after this. Null: no end.
    public DateTimeOffset? ValidUntil { get; set; }

    // Set when a code is withdrawn (a refunded gift, a promo stopped early). Plans already given stay.
    public DateTimeOffset? DisabledAt { get; set; }

    // What the team made it for ("Instagram, ноябрь"), never shown to couples.
    public string? Note { get; set; }
    public Guid? GiftOrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset PeriodEnd(DateTimeOffset from) => from.AddMonths(Months).AddDays(Days);
}

// One couple entering one code. Its Subscription row carries the plan.
public sealed class CodeRedemption
{
    public Guid Id { get; set; }
    public Guid CodeId { get; set; }
    public Guid UserId { get; set; }
    public Guid CoupleId { get; set; }
    public Guid SubscriptionId { get; set; }
    public DateTimeOffset RedeemedAt { get; set; }
}

public enum GiftOrderStatus { Pending, Paid, Cancelled, Refunded }

// Someone buying a gift subscription. Paid outside the stores (Kaspi on the website, monetization doc);
// the payment provider is the source of truth, as the stores are for subscriptions. A paid order gets
// its gift code.
public sealed class GiftOrder
{
    public Guid Id { get; set; }
    public Guid BuyerUserId { get; set; }
    public required string ProductId { get; set; }
    public CouplePlan Tier { get; set; }
    public int Months { get; set; }
    public int PriceKzt { get; set; }
    public GiftOrderStatus Status { get; set; }

    // The payment provider ("kaspi", "fake") and its id for this payment.
    public required string Provider { get; set; }
    public string? CheckoutId { get; set; }
    public string? PaymentUrl { get; set; }
    public Guid? CodeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
