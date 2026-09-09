using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RiffRoster;

namespace RiffRoster.IntegrationTests;

public sealed class PostgresTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static PreparedImport Source(string name = "store-v7.json") => JsonImport.Prepare(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name)));
    private async Task<string> Imported(string name = "store-v7.json")
    {
        var connection = await postgres.NewDatabase();
        using var db = RiffRosterDbContext.Connect(connection);
        db.Database.Migrate();
        Assert.True(JsonImport.Import(db, Source(name)).Matches);
        return connection;
    }

    [Theory]
    [InlineData("store-v5.json")]
    [InlineData("store-v7.json")]
    public async Task Migration_import_comparison_and_repeat_preserve_all_content(string fixture)
    {
        var connection = await Imported(fixture);
        var source = Source(fixture);
        using var db = RiffRosterDbContext.Connect(connection);
        Assert.Empty(db.Database.GetPendingMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.True(JsonImport.Compare(db, source).Matches);
        Assert.True(JsonImport.Import(db, source).Matches);
        Assert.Single(db.Set<ImportReceipt>());
        var original = source.State.Events.SelectMany(e => e.Days).SelectMany(d => d.Performances).Single();
        var saved = Assert.Single(db.Set<PerformanceRow>());
        Assert.Equal(original.Id, saved.LegacyId);
        Assert.Equal(original.StartsAt.UtcDateTime, saved.StartsAtUtc.UtcDateTime);
        Assert.Equal((int)original.StartsAt.Offset.TotalMinutes, saved.StartOffsetMinutes);
        Assert.Equal(StableImportId.For("performance", original.Id), saved.Id);
        Assert.Equal(source.Report.CanonicalSha256, StateAudit.Report(StateProjection.Read(db)).CanonicalSha256);
    }

    [Fact]
    public async Task Failed_final_import_write_rolls_back_every_table()
    {
        var connection = await postgres.NewDatabase();
        using (var db = RiffRosterDbContext.Connect(connection))
        {
            db.Database.Migrate();
            db.Database.ExecuteSqlRaw("""
                CREATE FUNCTION fail_import() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic rollback'; END $$;
                CREATE TRIGGER fail_import BEFORE INSERT ON "ImportReceipts" FOR EACH ROW EXECUTE FUNCTION fail_import();
                """);
            Assert.Throws<DbUpdateException>(() => JsonImport.Import(db, Source()));
        }
        using var fresh = RiffRosterDbContext.Connect(connection);
        Assert.Empty(fresh.Set<EventRow>());
        Assert.Empty(fresh.Set<AccountRow>());
        Assert.Empty(fresh.Set<SelectionRow>());
        Assert.Empty(fresh.Set<GroupRow>());
        Assert.Empty(fresh.Set<SubscriptionRow>());
        Assert.Empty(fresh.Set<DeliveryRow>());
        Assert.Empty(fresh.Set<ImportReceipt>());
    }

    [Fact]
    public async Task Empty_or_unmigrated_database_never_seeds_implicitly()
    {
        var connection = await postgres.NewDatabase();
        Assert.ThrowsAny<Exception>(() => new PostgresStatePersistence(connection).Open());
        using var db = RiffRosterDbContext.Connect(connection);
        db.Database.Migrate();
        Assert.Throws<InvalidOperationException>(() => new PostgresStatePersistence(connection).Open());
        Assert.Empty(db.Set<EventRow>());
    }

    [Fact]
    public async Task Edit_retains_uuid_and_picks_reschedules_reminders_and_delete_cascades()
    {
        var connection = await Imported();
        var store = new DataStore(new PostgresStatePersistence(connection));
        var start = DateTimeOffset.Parse("2026-08-04T22:00:00+02:00");
        var updated = store.UpdatePerformance("fixture-event", "fixture-performance", new("Changed", "New stage", start, start.AddHours(1)));
        Assert.Equal("fixture-performance", updated!.Id);
        using (var db = RiffRosterDbContext.Connect(connection))
        {
            Assert.Single(db.Set<SelectionRow>());
            Assert.Empty(db.Set<DeliveryRow>());
            Assert.Equal(StableImportId.For("performance", "fixture-performance"), Assert.Single(db.Set<PerformanceRow>()).Id);
            Assert.Equal("New stage", Assert.Single(db.Set<StageRow>()).Name);
        }
        var due = Assert.Single(store.GetDuePushReminders(start.AddMinutes(-10)));
        store.MarkReminderDelivered(due, start.AddMinutes(-10));
        var restarted = new DataStore(new PostgresStatePersistence(connection));
        Assert.Empty(restarted.GetDuePushReminders(start.AddMinutes(-10)));
        restarted.DeleteEvent("fixture-event");
        using var check = RiffRosterDbContext.Connect(connection);
        Assert.Empty(check.Set<PerformanceRow>());
        Assert.Empty(check.Set<SelectionRow>());
        Assert.Empty(check.Set<DeliveryRow>());
        Assert.Empty(check.Set<StageRow>());
        Assert.All(check.Set<AccountRow>(), a => Assert.Null(a.LastSelectedEventId));
    }

    [Fact]
    public async Task Independent_instances_do_not_lose_concurrent_picks_and_retry_never_overwrites_edits()
    {
        var connection = await Imported();
        var tasks = Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
            new DataStore(new PostgresStatePersistence(connection)).ToggleSelection("Fixture Bob", "fixture-performance")));
        await Task.WhenAll(tasks);
        using var db = RiffRosterDbContext.Connect(connection);
        Assert.Single(db.Set<SelectionRow>()); // Twelve toggles cancel; Alice's original pick survives.
        new DataStore(new PostgresStatePersistence(connection)).CreateGroup("Fixture Bob", "New crew");
        Assert.False(JsonImport.Compare(db, Source()).Matches);
        Assert.Throws<InvalidOperationException>(() => JsonImport.Import(db, Source()));
        db.ChangeTracker.Clear();
        Assert.Equal(2, db.Set<GroupRow>().Count());
    }

    [Fact]
    public async Task Database_constraints_reject_orphan_duplicate_and_nonpositive_duration()
    {
        var connection = await Imported();
        using (var db = RiffRosterDbContext.Connect(connection))
        {
            var existing = db.Set<SelectionRow>().Single();
            db.Add(new SelectionRow { Id = Guid.NewGuid(), AccountId = existing.AccountId, PerformanceId = existing.PerformanceId });
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
        using (var db = RiffRosterDbContext.Connect(connection))
        {
            db.Add(new SelectionRow { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), PerformanceId = db.Set<PerformanceRow>().Single().Id });
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
        using (var db = RiffRosterDbContext.Connect(connection))
        {
            var act = db.Set<PerformanceRow>().Single();
            act.EndsAtUtc = act.StartsAtUtc;
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
    }

    [Fact]
    public async Task Http_login_group_and_pick_contract_works_on_postgres()
    {
        var connection = await Imported();
        using var app = new PostgresApp(connection);
        using var client = app.CreateClient();
        var auth = await client.PostAsJsonAsync("/api/session", new { loginName = "private-fixture", displayName = "Fixture Alice", password = "fixture-password-only", accessCode = "friends-local" });
        auth.EnsureSuccessStatusCode();
        var state = await client.GetStringAsync("/api/state");
        Assert.Contains("Fixture Bob", state);
        Assert.DoesNotContain("private-fixture", state);
        Assert.DoesNotContain("synthetic-key", state);
        (await client.PostAsJsonAsync("/api/selections/toggle", new { performanceId = "fixture-performance" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/api/events/fixture-event")).StatusCode);
        using var db = RiffRosterDbContext.Connect(connection);
        Assert.Empty(db.Set<SelectionRow>());
        Assert.Empty(db.Set<DeliveryRow>());
        Assert.True(PasswordCredentials.Verify("fixture-password-only", db.Set<AccountRow>().Single(a => a.DisplayName == "Fixture Alice").PasswordSalt,
            db.Set<AccountRow>().Single(a => a.DisplayName == "Fixture Alice").PasswordHash));
    }

    [Fact]
    public async Task Daily_backup_restore_and_corruption_gate_preserve_source_database()
    {
        var connection = await Imported();
        var directory = Path.Combine(Path.GetTempPath(), "riff-backup-test-" + Guid.NewGuid().ToString("N"));
        var binaries = Environment.GetEnvironmentVariable("RIFFROSTER_PG_BIN");
        try
        {
            var dump = PostgresBackup.CreateDaily(connection, directory, binaries);
            var before = File.ReadAllBytes(dump);
            Assert.Equal(dump, PostgresBackup.CreateDaily(connection, directory, binaries));
            Assert.Equal(before, File.ReadAllBytes(dump));
            var restored = PostgresBackup.RestoreDrill(connection, dump, binaries);
            Assert.Equal(Source().Report.CanonicalSha256, restored.CanonicalSha256);
            using var source = RiffRosterDbContext.Connect(connection);
            Assert.True(JsonImport.Compare(source, Source()).Matches);
            File.AppendAllText(dump, "corrupt-fixture");
            Assert.Throws<InvalidDataException>(() => PostgresBackup.RestoreDrill(connection, dump, binaries));
            Assert.True(JsonImport.Compare(source, Source()).Matches);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Maintenance_mode_keeps_reads_and_rejects_mutations_without_starting_reminders()
    {
        var connection = await Imported();
        using var app = new PostgresApp(connection, readOnly: true);
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        var response = await client.PostAsJsonAsync("/api/session", new { loginName = "fixture-new" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.RetryAfter!.Delta);
        using var db = RiffRosterDbContext.Connect(connection);
        Assert.True(JsonImport.Compare(db, Source()).Matches);
    }

    [Fact]
    public async Task Checked_in_schema_can_be_reverted_and_reapplied_in_an_empty_test_database()
    {
        var connection = await postgres.NewDatabase();
        using var db = RiffRosterDbContext.Connect(connection);
        db.Database.Migrate();
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
        migrator.Migrate("0");
        Assert.Equal(2, db.Database.GetPendingMigrations().Count());
        db.Database.Migrate();
        Assert.True(JsonImport.Import(db, Source()).Matches);
    }

    [Fact]
    public async Task Reverse_export_reopens_in_json_provider_with_new_postgres_writes_preserved()
    {
        var connection = await Imported();
        new DataStore(new PostgresStatePersistence(connection)).CreateGroup("Fixture Bob", "After cutover");
        var directory = Path.Combine(Path.GetTempPath(), "riff-reverse-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var db = RiffRosterDbContext.Connect(connection);
            var state = StateProjection.Read(db);
            var expected = StateAudit.Report(state);
            var path = Path.Combine(directory, "reverse.json");
            File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.Equal(expected.CanonicalSha256, JsonImport.ReadSource(path).Report.CanonicalSha256);
            using var app = new PostgresApp(connection, readOnly: true, jsonPath: path);
            using var session = app.Services.GetRequiredService<IStatePersistence>().Open();
            Assert.Equal(expected.CanonicalSha256, StateAudit.Report(session.State).CanonicalSha256);
            Assert.Equal(2, session.State.Groups.Count);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Import_preserves_every_legacy_timestamp_tick_including_before_postgres_epoch()
    {
        var state = Source().State;
        state.Users[0].JoinedAt = DateTimeOffset.Parse("1999-12-31T23:59:59.1234567Z");
        var day = state.Events[0].Days[0];
        day.Performances[0] = day.Performances[0] with { StartsAt = day.Performances[0].StartsAt.AddTicks(1), EndsAt = day.Performances[0].EndsAt.AddTicks(2) };
        state.Groups[0] = state.Groups[0] with { CreatedAt = state.Groups[0].CreatedAt.AddTicks(3) };
        state.GroupMemberships[0] = state.GroupMemberships[0] with { JoinedAt = state.GroupMemberships[0].JoinedAt.AddTicks(4) };
        state.PushDevices[0] = state.PushDevices[0] with { CreatedAt = state.PushDevices[0].CreatedAt.AddTicks(5), LastSeenAt = state.PushDevices[0].LastSeenAt.AddTicks(6) };
        state.ReminderDeliveries[0] = state.ReminderDeliveries[0] with { PerformanceStartsAt = state.ReminderDeliveries[0].PerformanceStartsAt.AddTicks(1), SentAt = state.ReminderDeliveries[0].SentAt.AddTicks(8) };
        state.ReminderDeliveries.Add(state.ReminderDeliveries[0] with { PerformanceStartsAt = state.ReminderDeliveries[0].PerformanceStartsAt.AddTicks(1) });
        var source = JsonImport.Prepare(JsonSerializer.SerializeToUtf8Bytes(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var db = RiffRosterDbContext.Connect(await postgres.NewDatabase());
        db.Database.Migrate();
        Assert.True(JsonImport.Import(db, source).Matches);
        Assert.True(JsonImport.Compare(db, source).Matches);
        new DataStore(new PostgresStatePersistence(db.Database.GetConnectionString()!)).CreateGroup("Fixture Bob", "Subsequent write");
        db.ChangeTracker.Clear();
        var restored = StateProjection.Read(db);
        Assert.Equal(state.Users[0].JoinedAt, restored.Users[0].JoinedAt);
        Assert.Equal(state.PushDevices[0].LastSeenAt, restored.PushDevices[0].LastSeenAt);
        Assert.Equal(state.ReminderDeliveries, restored.ReminderDeliveries);
    }

    private sealed class PostgresApp(string connection, bool readOnly = false, string? jsonPath = null) : WebApplicationFactory<Program>
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "riff-postgres-http-" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("RIFFROSTER_READ_ONLY", readOnly.ToString());
            builder.UseSetting("RIFFROSTER_KEYS_PATH", _directory);
            builder.UseSetting("RIFFROSTER_INVITE_CODE", "friends-local");
            builder.UseSetting("RIFFROSTER_ADMIN_CODE", "admin-local-only");
            builder.UseSetting("RIFFROSTER_STORAGE", jsonPath is null ? "Postgres" : "Json");
            if (jsonPath is not null)
            {
                builder.UseSetting("RIFFROSTER_DATA_PATH", jsonPath);
                builder.UseSetting("RIFFROSTER_BACKUP_DIR", Path.Combine(_directory, "backups"));
            }
            builder.UseSetting("RIFFROSTER_POSTGRES_CONNECTION", connection);
            builder.UseSetting("RIFFROSTER_VAPID_PUBLIC_KEY", "");
            builder.UseSetting("RIFFROSTER_VAPID_PRIVATE_KEY", "");
            builder.UseSetting("RIFFROSTER_VAPID_SUBJECT", "");
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }
}
