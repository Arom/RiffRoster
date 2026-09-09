using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RiffRoster;

public sealed class RiffRosterDbContext(DbContextOptions<RiffRosterDbContext> options) : DbContext(options)
{
    public static RiffRosterDbContext Connect(string connection) => new(new DbContextOptionsBuilder<RiffRosterDbContext>()
        .UseNpgsql(connection, o => o.CommandTimeout(60)).Options);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<EventRow>(e =>
        {
            e.ToTable("Events", t => t.HasCheckConstraint("CK_Event_Dates", "\"EndDate\" >= \"StartDate\""));
            e.HasKey(x => x.Id); e.HasIndex(x => x.LegacyId).IsUnique();
        });
        b.Entity<DayRow>(e =>
        {
            e.ToTable("Days"); e.HasKey(x => x.Id); e.HasIndex(x => x.LegacyId).IsUnique();
            e.HasAlternateKey(x => new { x.Id, x.EventId });
            e.HasIndex(x => new { x.EventId, x.Date }).IsUnique();
            e.HasOne<EventRow>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<StageRow>(e =>
        {
            e.ToTable("Stages"); e.HasKey(x => x.Id); e.HasAlternateKey(x => new { x.Id, x.EventId });
            e.HasIndex(x => new { x.EventId, x.Name }).IsUnique();
            e.HasOne<EventRow>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PerformanceRow>(e =>
        {
            e.ToTable("Performances", t =>
            {
                t.HasCheckConstraint("CK_Performance_Duration", "(\"EndsAtUtc\", \"EndsAtUtcTickRemainder\") > (\"StartsAtUtc\", \"StartsAtUtcTickRemainder\")");
                t.HasCheckConstraint("CK_Performance_Offsets", "\"StartOffsetMinutes\" BETWEEN -840 AND 840 AND \"EndOffsetMinutes\" BETWEEN -840 AND 840");
            });
            e.HasKey(x => x.Id); e.HasIndex(x => x.LegacyId).IsUnique();
            e.HasOne<DayRow>().WithMany().HasForeignKey(x => new { x.DayId, x.EventId }).HasPrincipalKey(x => new { x.Id, x.EventId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<StageRow>().WithMany().HasForeignKey(x => new { x.StageId, x.EventId }).HasPrincipalKey(x => new { x.Id, x.EventId }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AccountRow>(e =>
        {
            e.ToTable("Accounts"); e.HasKey(x => x.Id); e.HasIndex(x => x.LegacyId).IsUnique();
            e.HasIndex(x => x.NormalizedName).IsUnique(); e.HasIndex(x => x.NormalizedLogin).IsUnique();
            e.HasOne<EventRow>().WithMany().HasForeignKey(x => x.LastSelectedEventId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<SelectionRow>(e =>
        {
            e.ToTable("Selections"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.AccountId, x.PerformanceId }).IsUnique();
            e.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PerformanceRow>().WithMany().HasForeignKey(x => x.PerformanceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<GroupRow>(e =>
        {
            e.ToTable("Groups"); e.HasKey(x => x.Id); e.HasIndex(x => x.LegacyId).IsUnique();
            e.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<MembershipRow>(e =>
        {
            e.ToTable("Memberships"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.GroupId, x.AccountId }).IsUnique();
            e.HasOne<GroupRow>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SubscriptionRow>(e =>
        {
            e.ToTable("Subscriptions", t => t.HasCheckConstraint("CK_Subscription_Lead", "\"LeadMinutes\" IN (10,20,30,60)"));
            e.HasKey(x => x.Id); e.HasIndex(x => x.LegacyId).IsUnique(); e.HasIndex(x => x.Endpoint).IsUnique();
            e.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DeliveryRow>(e =>
        {
            e.ToTable("Deliveries"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SubscriptionId, x.PerformanceId, x.PerformanceStartsAt, x.PerformanceStartsAtTickRemainder }).IsUnique();
            e.HasOne<SubscriptionRow>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PerformanceRow>().WithMany().HasForeignKey(x => x.PerformanceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ImportReceipt>(e =>
        {
            e.ToTable("ImportReceipts"); e.HasKey(x => x.Id); e.HasIndex(x => x.SourceSha256).IsUnique();
        });
        foreach (var entity in b.Model.GetEntityTypes().ToList())
            foreach (var property in entity.GetProperties().Where(p => p.Name.EndsWith("TickRemainder", StringComparison.Ordinal)).ToList())
                b.Entity(entity.ClrType).ToTable(t => t.HasCheckConstraint($"CK_{entity.GetTableName()}_{property.Name}", $"\"{property.Name}\" BETWEEN 0 AND 9"));
    }
}

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RiffRosterDbContext>
{
    // No live credentials required to generate/inspect migrations.
    public RiffRosterDbContext CreateDbContext(string[] args) => RiffRosterDbContext.Connect("Host=localhost;Database=design_only;Username=design_only");
}
