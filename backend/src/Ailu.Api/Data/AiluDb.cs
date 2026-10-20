using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Data;

public sealed class AiluDb(DbContextOptions<AiluDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<PhoneCode> PhoneCodes => Set<PhoneCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Couple> Couples => Set<Couple>();
    public DbSet<CoupleMember> CoupleMembers => Set<CoupleMember>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<DailyTask> DailyTasks => Set<DailyTask>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<CoupleTask> CoupleTasks => Set<CoupleTask>();
    public DbSet<DailyQuestion> DailyQuestions => Set<DailyQuestion>();
    public DbSet<CoupleActivity> CoupleActivities => Set<CoupleActivity>();
    public DbSet<NurLedgerEntry> NurLedger => Set<NurLedgerEntry>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PushLog> PushLog => Set<PushLog>();
    public DbSet<Memory> Memories => Set<Memory>();
    public DbSet<MemoryPhoto> MemoryPhotos => Set<MemoryPhoto>();
    public DbSet<MemoryReaction> MemoryReactions => Set<MemoryReaction>();
    public DbSet<MoodEntry> Moods => Set<MoodEntry>();
    public DbSet<MoodMonth> MoodMonths => Set<MoodMonth>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalStep> GoalSteps => Set<GoalStep>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<LoveQuizRound> LoveQuizRounds => Set<LoveQuizRound>();
    public DbSet<LoveQuizAnswer> LoveQuizAnswers => Set<LoveQuizAnswer>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<CoupleAchievement> CoupleAchievements => Set<CoupleAchievement>();
    public DbSet<PhotoBook> PhotoBooks => Set<PhotoBook>();
    public DbSet<PhotoBookSpread> PhotoBookSpreads => Set<PhotoBookSpread>();
    public DbSet<PhotoBookOrder> PhotoBookOrders => Set<PhotoBookOrder>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Phone).IsUnique();
            e.Property(x => x.Phone).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Locale).HasMaxLength(5);
            e.Property(x => x.TimeZone).HasMaxLength(64);
        });

        b.Entity<PhoneCode>(e =>
        {
            e.HasIndex(x => new { x.Phone, x.CreatedAt });
            e.Property(x => x.Phone).HasMaxLength(16);
        });

        b.Entity<RefreshToken>(e => e.HasIndex(x => x.TokenHash).IsUnique());

        b.Entity<Couple>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Plan).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.TimeZone).HasMaxLength(64);
            e.HasMany(x => x.Members).WithOne(m => m.Couple).HasForeignKey(m => m.CoupleId);
        });

        b.Entity<CoupleMember>(e =>
        {
            e.HasKey(x => new { x.CoupleId, x.UserId });
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(1);
            // A person can be in at most one current couple.
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("\"LeftAt\" IS NULL");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<Invitation>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Phone).HasMaxLength(16);
            e.Property(x => x.Code).HasMaxLength(6);
        });

        b.Entity<DailyTask>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasData(Seed.Tasks);
        });

        b.Entity<Question>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasData(Seed.Questions);
        });

        b.Entity<CoupleTask>(e =>
        {
            e.HasIndex(x => new { x.CoupleId, x.Date, x.Ordinal }).IsUnique();
            e.HasOne(x => x.DailyTask).WithMany().HasForeignKey(x => x.DailyTaskId);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
        });

        b.Entity<DailyQuestion>(e =>
        {
            e.HasIndex(x => new { x.CoupleId, x.Date }).IsUnique();
            e.Property(x => x.AnswerA).HasMaxLength(1000);
            e.Property(x => x.AnswerB).HasMaxLength(1000);
            e.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
        });

        b.Entity<CoupleActivity>(e =>
        {
            e.HasKey(x => new { x.CoupleId, x.Date, x.UserId });
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
        });

        b.Entity<NurLedgerEntry>(e =>
        {
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.CoupleId);
            e.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SourceId).HasMaxLength(64);
            e.Property(x => x.IdempotencyKey).HasMaxLength(160);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
        });

        b.Entity<Device>(e =>
        {
            e.HasIndex(x => x.PushToken).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.PushToken).HasMaxLength(200);
            e.Property(x => x.Platform).HasMaxLength(10);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<PushLog>(e =>
        {
            e.HasKey(x => new { x.UserId, x.Date, x.Kind });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<Memory>(e =>
        {
            e.HasIndex(x => new { x.CoupleId, x.HappenedOn, x.CreatedAt });
            e.HasIndex(x => x.DeletedAt).HasFilter("\"DeletedAt\" IS NOT NULL");
            e.Property(x => x.Title).HasMaxLength(120);
            e.Property(x => x.Text).HasMaxLength(2000);
            e.Property(x => x.Place).HasMaxLength(120);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);
            e.HasOne<CoupleTask>().WithMany().HasForeignKey(x => x.CoupleTaskId);
            e.HasMany(x => x.Photos).WithOne().HasForeignKey(p => p.MemoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Reactions).WithOne().HasForeignKey(r => r.MemoryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MemoryPhoto>(e =>
        {
            e.HasIndex(x => new { x.MemoryId, x.Position }).IsUnique();
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
        });

        b.Entity<MemoryReaction>(e =>
        {
            e.HasKey(x => new { x.MemoryId, x.UserId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<MoodEntry>(e =>
        {
            e.HasKey(x => new { x.CoupleId, x.Date, x.UserId });
            e.HasIndex(x => x.Date);
            e.Property(x => x.Mood).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Note).HasMaxLength(280);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<MoodMonth>(e =>
        {
            e.HasKey(x => new { x.CoupleId, x.UserId, x.Month });
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<Goal>(e =>
        {
            e.HasIndex(x => x.CoupleId);
            e.Property(x => x.Title).HasMaxLength(80);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(10);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById);
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(s => s.GoalId);
        });

        b.Entity<GoalStep>(e =>
        {
            e.HasIndex(x => new { x.GoalId, x.Position });
            e.Property(x => x.Title).HasMaxLength(120);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
        });

        b.Entity<LoveQuizRound>(e =>
        {
            e.HasIndex(x => new { x.CoupleId, x.StartedAt });
            // At most one round waiting for answers per couple.
            e.HasIndex(x => x.CoupleId).IsUnique().HasFilter("\"CompletedAt\" IS NULL");
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasMany(x => x.Answers).WithOne().HasForeignKey(a => a.RoundId);
        });

        b.Entity<LoveQuizAnswer>(e =>
        {
            e.HasKey(x => new { x.RoundId, x.UserId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<Subscription>(e =>
        {
            // One row per store subscription: a second couple can never claim the same purchase.
            e.HasIndex(x => new { x.Provider, x.StoreId }).IsUnique();
            e.HasIndex(x => x.CoupleId);
            e.HasIndex(x => x.PayerUserId);
            e.HasIndex(x => x.PeriodEnd);
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Tier).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.StoreId).HasMaxLength(500);
            e.Property(x => x.ProductId).HasMaxLength(100);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.PayerUserId);
        });

        b.Entity<Achievement>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(40);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Tier).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Icon).HasMaxLength(16);
            e.Property(x => x.Title).HasMaxLength(60);
            e.Property(x => x.TitleKk).HasMaxLength(60);
            e.Property(x => x.Body).HasMaxLength(200);
            e.Property(x => x.BodyKk).HasMaxLength(200);
            e.HasData(Seed.Achievements);
        });

        b.Entity<CoupleAchievement>(e =>
        {
            e.HasKey(x => new { x.CoupleId, x.AchievementId });
            e.HasOne(x => x.Achievement).WithMany().HasForeignKey(x => x.AchievementId);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.MarkedById);
            e.HasOne<Memory>().WithMany().HasForeignKey(x => x.MemoryId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PhotoBook>(e =>
        {
            e.HasIndex(x => new { x.CoupleId, x.Year }).IsUnique();
            e.Property(x => x.Title).HasMaxLength(80);
            e.Property(x => x.Subtitle).HasMaxLength(80);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasMany(x => x.Spreads).WithOne().HasForeignKey(s => s.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PhotoBookSpread>(e =>
        {
            e.HasKey(x => new { x.BookId, x.Position });
            e.Property(x => x.Layout).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Title).HasMaxLength(250);
            e.Property(x => x.Text).HasMaxLength(1000);
        });

        b.Entity<PhotoBookOrder>(e =>
        {
            e.HasIndex(x => x.CoupleId);
            e.HasIndex(x => x.PaymentId).IsUnique();
            e.HasIndex(x => x.PrintJobId).IsUnique();
            e.HasIndex(x => x.Status);
            // At most one order of a book waiting for payment: the book is locked to it until it is paid or cancelled.
            e.HasIndex(x => x.BookId).IsUnique().HasFilter("\"Status\" = 'AwaitingPayment'");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RecipientName).HasMaxLength(100);
            e.Property(x => x.Phone).HasMaxLength(16);
            e.Property(x => x.City).HasMaxLength(60);
            e.Property(x => x.Address).HasMaxLength(250);
            e.Property(x => x.Comment).HasMaxLength(250);
            e.Property(x => x.Content).HasColumnType("jsonb");
            e.Property(x => x.PaymentId).HasMaxLength(100);
            e.Property(x => x.PayUrl).HasMaxLength(500);
            e.Property(x => x.PrintJobId).HasMaxLength(100);
            e.Property(x => x.TrackingNumber).HasMaxLength(100);
            e.HasOne<PhotoBook>().WithMany().HasForeignKey(x => x.BookId);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OrderedById);
        });
    }
}
