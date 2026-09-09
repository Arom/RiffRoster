using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RiffRoster;

namespace RiffRoster.CharacterizationTests;

public sealed class StoreTests
{
    [Fact]
    public void Seed_has_164_unique_positive_duration_slots_and_after_midnight_stays_on_previous_day()
    {
        var seed = SeedData.Create();
        var festival = Assert.Single(seed.Events);
        Assert.Equal(5, festival.Days.Count);
        var acts = festival.Days.SelectMany(d => d.Performances).ToList();
        Assert.Equal(164, acts.Count);
        Assert.Equal(acts.Count, acts.Select(p => p.Id).Distinct().Count());
        Assert.All(acts, p => Assert.True(p.EndsAt > p.StartsAt));
        var overnight = Assert.Single(festival.Days[0].Performances, p => p.Artist == "Bolehlav");
        Assert.Equal(new DateTime(2026, 8, 5), overnight.EndsAt.Date);
        Assert.Contains(acts, p => p.Stage == "KAL");
        Assert.Empty(seed.Users);
        Assert.Empty(seed.Selections);
    }

    [Fact]
    public void Legacy_fixture_migrates_and_claim_preserves_picks_and_ids()
    {
        using var app = new TestApp("store-v5.json");
        var result = app.Store.SignInOrRegister("private-legacy", "Legacy attendee", "fixture-password-only");
        Assert.True(result.Claimed);
        var state = app.ReadState();
        Assert.Equal(7, state.DataVersion);
        Assert.Equal("legacy-performance", Assert.Single(state.Selections).PerformanceId);
        Assert.Equal("legacy-event", Assert.Single(state.Users).LastSelectedEventId);
        Assert.Empty(state.Groups);
        Assert.True(PasswordCredentials.Verify("fixture-password-only", state.Users[0].PasswordSalt, state.Users[0].PasswordHash));
        Assert.DoesNotContain("fixture-password-only", File.ReadAllText(app.StorePath));
    }

    [Fact]
    public void Backup_is_once_daily_byte_identical_and_restores_all_collections_without_reseeding()
    {
        using var source = new TestApp("store-v7.json");
        var original = File.ReadAllBytes(source.StorePath);
        Assert.True(source.Store.CreateBackup());
        Assert.False(source.Store.CreateBackup());
        var snapshot = Assert.Single(Directory.GetFiles(source.BackupPath, "store-*.json"));
        Assert.Equal(original, File.ReadAllBytes(snapshot));
        using var restored = new TestApp();
        File.Copy(snapshot, restored.StorePath);
        var visible = TestApp.Element(restored.Store.GetSnapshot("Fixture Alice", false));
        Assert.Equal("fixture-event", visible.GetProperty("events")[0].GetProperty("id").GetString());
        Assert.Equal(2, visible.GetProperty("users").GetArrayLength());
        Assert.Equal(original, File.ReadAllBytes(restored.StorePath));
        var state = restored.ReadState();
        Assert.Single(state.Selections);
        Assert.Single(state.PushDevices);
        Assert.Single(state.ReminderDeliveries);
        Assert.Single(state.Groups);
        Assert.Equal(2, state.GroupMemberships.Count);
    }

    [Fact]
    public void Backup_retains_fourteen_daily_snapshots_and_leaves_invalid_evidence_alone()
    {
        using var app = new TestApp("store-v7.json");
        _ = app.Store;
        for (var days = 1; days <= 20; days++)
            File.Copy(app.StorePath, Path.Combine(app.BackupPath, $"store-{DateTime.UtcNow.AddDays(-days):yyyyMMdd}.json"));
        var evidence = Path.Combine(app.BackupPath, "invalid-fixture.json");
        File.WriteAllText(evidence, "synthetic invalid evidence");
        Assert.True(app.Store.CreateBackup());
        Assert.Equal(14, Directory.GetFiles(app.BackupPath, "store-*.json").Length);
        Assert.True(File.Exists(evidence));
        Assert.False(File.Exists(Path.Combine(app.BackupPath, $"store-{DateTime.UtcNow.AddDays(-20):yyyyMMdd}.json")));
    }

    [Fact]
    public void Malformed_store_stops_startup_and_preserves_original_bytes()
    {
        using var app = new TestApp();
        File.WriteAllText(app.StorePath, "{broken-fixture");
        Assert.Throws<InvalidDataException>(() => app.Store);
        Assert.Equal("{broken-fixture", File.ReadAllText(app.StorePath));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"dataVersion\":4}")]
    [InlineData("{\"dataVersion\":8}")]
    [InlineData("{\"dataVersion\":7}")]
    public void Unsupported_or_incomplete_store_never_seeds(string source)
    {
        using var app = new TestApp();
        File.WriteAllText(app.StorePath, source);
        Assert.Throws<InvalidDataException>(() => app.Store);
        Assert.Equal(source, File.ReadAllText(app.StorePath));
    }

    [Fact]
    public void Legacy_upgrade_preserves_a_byte_identical_source_backup()
    {
        using var app = new TestApp("store-v5.json");
        var original = File.ReadAllBytes(app.StorePath);
        _ = app.Store;
        Assert.Equal(original, File.ReadAllBytes(Assert.Single(Directory.GetFiles(app.BackupPath, "before-v5-upgrade-*.json"))));
        Assert.Equal(7, app.ReadState().DataVersion);
    }

    [Fact]
    public void Failed_disk_replacement_does_not_publish_memory_and_retry_persists()
    {
        using var app = new TestApp("store-v7.json");
        var store = app.Store;
        var original = File.ReadAllBytes(app.StorePath);
        var held = app.StorePath + ".held";
        File.Move(app.StorePath, held);
        Directory.CreateDirectory(app.StorePath); // Deterministic rename failure on both Windows and Linux.
        var failure = Record.Exception(() => store.ToggleSelection("Fixture Bob", "fixture-performance"));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Directory.Delete(app.StorePath);
        File.Move(held, app.StorePath);
        using (var state = app.Services.GetRequiredService<IStatePersistence>().Open())
            Assert.Single(state.State.Selections);
        Assert.Equal(original, File.ReadAllBytes(app.StorePath));
        Assert.Empty(Directory.GetFiles(app.DirectoryPath, "*.tmp"));
        store.ToggleSelection("Fixture Bob", "fixture-performance");
        using var restarted = new TestApp();
        File.Copy(app.StorePath, restarted.StorePath);
        using var fresh = restarted.Services.GetRequiredService<IStatePersistence>().Open();
        Assert.Equal(2, fresh.State.Selections.Count);
    }

    [Fact]
    public void Unsaved_session_changes_are_discarded_and_invalid_backups_are_reported()
    {
        using var app = new TestApp("store-v7.json");
        var persistence = app.Services.GetRequiredService<IStatePersistence>();
        using (var session = persistence.Open()) session.State.Selections.Clear();
        using (var session = persistence.Open()) Assert.Single(session.State.Selections);
        File.WriteAllText(Path.Combine(app.BackupPath, $"store-{DateTime.UtcNow:yyyyMMdd}.json"), "{broken-backup");
        Assert.ThrowsAny<Exception>(() => app.Store.CreateBackup());
        Assert.Single(app.ReadState().Selections);
    }

    [Fact]
    public void Reminder_delivery_survives_restart_edit_requeues_and_subscription_ownership_is_enforced()
    {
        using var app = new TestApp("store-v7.json");
        var store = app.Store;
        // Remove fixture history to model a due reminder, then exercise real persistence.
        store.RemovePushSubscription("Fixture Bob", "https://push.example.test/fixture");
        Assert.NotNull(store.GetPushDevice("Fixture Alice", "https://push.example.test/fixture"));
        Assert.Null(store.GetPushDevice("Fixture Bob", "https://push.example.test/fixture"));
        var start = DateTimeOffset.Parse("2026-08-04T20:10:00+02:00");
        store.UpdatePerformance("fixture-event", "fixture-performance", new("Changed act", "Main", start, start.AddHours(1)));
        store.ToggleSelection("Fixture Bob", "fixture-performance");
        var now = start.AddMinutes(-10);
        Assert.Empty(store.GetDuePushReminders(start.AddMinutes(-21)));
        var due = Assert.Single(store.GetDuePushReminders(now));
        Assert.Equal(new[] { "Fixture Bob" }, due.CrewNames);
        store.MarkReminderDelivered(due, now);
        store.MarkReminderDelivered(due, now);
        Assert.Single(app.ReadState().ReminderDeliveries);
        Assert.Empty(store.GetDuePushReminders(now));
        using var restarted = new TestApp();
        File.Copy(app.StorePath, restarted.StorePath);
        Assert.Empty(restarted.Store.GetDuePushReminders(now));
        Assert.Empty(restarted.Store.GetDuePushReminders(start));
        store.DeletePerformance("fixture-event", "fixture-performance");
        Assert.Empty(app.ReadState().Selections);
        Assert.Empty(app.ReadState().ReminderDeliveries);
    }

    [Theory]
    [InlineData("http://push.example.test/device", 20)]
    [InlineData("https://127.0.0.1/device", 20)]
    [InlineData("https://192.168.1.1/device", 20)]
    [InlineData("https://device.local/device", 20)]
    [InlineData("https://push.example.test/device", 15)]
    public void Push_boundary_rejects_known_invalid_endpoints_and_preferences(string endpoint, int lead)
        => Assert.NotNull(PushSubscriptionValidator.Validate(new(endpoint, new("key", "auth"), lead, false)));
}
