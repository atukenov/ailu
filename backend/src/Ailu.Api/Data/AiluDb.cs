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
    public DbSet<PlanCode> PlanCodes => Set<PlanCode>();
    public DbSet<CodeRedemption> CodeRedemptions => Set<CodeRedemption>();
    public DbSet<GiftOrder> GiftOrders => Set<GiftOrder>();
    public DbSet<LoveQuizRound> LoveQuizRounds => Set<LoveQuizRound>();
    public DbSet<LoveQuizAnswer> LoveQuizAnswers => Set<LoveQuizAnswer>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<CoupleAchievement> CoupleAchievements => Set<CoupleAchievement>();
    public DbSet<YearStory> YearStories => Set<YearStory>();
    public DbSet<AiPick> AiPicks => Set<AiPick>();
    public DbSet<AiPickItem> AiPickItems => Set<AiPickItem>();

    // The shared task library, without the personal tasks AI wrote for single couples.
    public IQueryable<DailyTask> TaskLibrary => DailyTasks.Where(t => t.CoupleId == null);

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
            e.HasIndex(x => x.CoupleId);
            e.Property(x => x.Slug).HasMaxLength(60);
            e.Property(x => x.Category).HasMaxLength(30);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
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
            // The archive job looks for old photos still in the standard class.
            e.HasIndex(x => x.CreatedAt).HasFilter("\"ArchivedAt\" IS NULL");
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

        b.Entity<PlanCode>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.GiftOrderId).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Tier).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Note).HasMaxLength(200);
        });

        b.Entity<CodeRedemption>(e =>
        {
            // Each person and each couple enters a code once. The same couple asking twice at once, or the
            // partner after the other, is turned away by the database, not only by the check before.
            e.HasIndex(x => new { x.CodeId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.CodeId, x.CoupleId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<PlanCode>().WithMany().HasForeignKey(x => x.CodeId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasOne<Subscription>().WithMany().HasForeignKey(x => x.SubscriptionId);
        });

        b.Entity<GiftOrder>(e =>
        {
            e.HasIndex(x => x.BuyerUserId);
            e.HasIndex(x => new { x.Provider, x.CheckoutId }).IsUnique();
            e.Property(x => x.ProductId).HasMaxLength(40);
            e.Property(x => x.Tier).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Provider).HasMaxLength(20);
            e.Property(x => x.CheckoutId).HasMaxLength(200);
            e.Property(x => x.PaymentUrl).HasMaxLength(1000);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BuyerUserId);
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

        b.Entity<AiPick>(e =>
        {
            e.HasIndex(x => new { x.CoupleId, x.CreatedAt });
            e.HasIndex(x => new { x.CoupleId, x.Month });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Budget).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Note).HasMaxLength(140);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedById);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.PickId);
        });

        b.Entity<AiPickItem>(e =>
        {
            e.HasIndex(x => new { x.PickId, x.Position }).IsUnique();
            e.HasIndex(x => new { x.CoupleId, x.SavedAt }).HasFilter("\"SavedAt\" IS NOT NULL");
            e.Property(x => x.Title).HasMaxLength(120);
            e.Property(x => x.TitleKk).HasMaxLength(120);
            e.Property(x => x.Body).HasMaxLength(600);
            e.Property(x => x.BodyKk).HasMaxLength(600);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SavedById);
        });

        b.Entity<CoupleAchievement>(e =>
        {
            e.HasKey(x => new { x.CoupleId, x.AchievementId });
            e.HasOne(x => x.Achievement).WithMany().HasForeignKey(x => x.AchievementId);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.MarkedById);
            e.HasOne<Memory>().WithMany().HasForeignKey(x => x.MemoryId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<YearStory>(e =>
        {
            // One film per couple per year, and one purchase can pay for only one film.
            e.HasIndex(x => new { x.CoupleId, x.Year }).IsUnique();
            e.HasIndex(x => new { x.Provider, x.StoreId }).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.StoreId).HasMaxLength(500);
            e.Property(x => x.ProductId).HasMaxLength(100);
            e.Property(x => x.Locale).HasMaxLength(5);
            e.Property(x => x.Storyboard).HasColumnType("jsonb");
            e.Property(x => x.Error).HasMaxLength(500);
            e.HasOne<Couple>().WithMany().HasForeignKey(x => x.CoupleId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.PayerUserId);
        });
    }
}
