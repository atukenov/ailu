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
            e.HasIndex(x => new { x.CoupleId, x.Date }).IsUnique();
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
    }
}
