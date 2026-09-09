using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace RiffRoster;

public sealed record PreparedImport(AppState State, int SourceVersion, string SourceSha256, StateReport Report);
public sealed record ComparisonReport(bool Matches, StateReport Source, StateReport Database);

public static class JsonImport
{
    public static PreparedImport ReadSource(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 100 * 1024 * 1024) throw new InvalidDataException("Source exceeds the migration size limit.");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return Prepare(bytes);
    }
    public static PreparedImport Prepare(byte[] bytes)
    {
        if (bytes.Length > 100 * 1024 * 1024) throw new InvalidDataException("Source exceeds the migration size limit.");
        ReadOnlyMemory<byte> content = bytes;
        if (content.Span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) content = content[3..];
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Source must be an object.");
        CheckDuplicates(root);
        var version = root.GetProperty("dataVersion").GetInt32();
        if (version is < 5 or > 7) throw new InvalidDataException("Only characterized versions 5–7 may be imported.");
        foreach (var field in new[] { "events", "users", "selections", "pushDevices", "reminderDeliveries", "groups", "groupMemberships" })
        {
            if (version == 5 && field is "groups" or "groupMemberships" && !root.TryGetProperty(field, out _)) continue;
            if (!root.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Required collection missing or invalid.");
        }
        var state = JsonSerializer.Deserialize<AppState>(content.Span, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        })!;
        // Only the documented v5/v6 forward defaults; never run the old seed migrations.
        if (version < 7)
            foreach (var user in state.Users.Where(u => string.IsNullOrWhiteSpace(u.LastSelectedEventId)))
                user.LastSelectedEventId = state.Events.FirstOrDefault()?.Id;
        state.DataVersion = 7;
        StateAudit.Validate(state);
        // Legacy references compare names case-insensitively. Canonicalize references, not display names.
        var names = state.Users.ToDictionary(u => u.Username, u => u.Username, StringComparer.OrdinalIgnoreCase);
        state.Selections = state.Selections.Select(s => s with { Username = names[s.Username] }).ToList();
        state.GroupMemberships = state.GroupMemberships.Select(m => m with { Username = names[m.Username] }).ToList();
        state.Groups = state.Groups.Select(g => g with { CreatedBy = names[g.CreatedBy] }).ToList();
        state.PushDevices = state.PushDevices.Select(d => d with { Username = names[d.Username] }).ToList();
        return new PreparedImport(state, version, Convert.ToHexString(SHA256.HashData(bytes)), StateAudit.Report(state));
    }

    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property.");
                if (property.Name.ToLowerInvariant() is "startsat" or "endsat" or "joinedat" or "createdat" or "lastseenat" or "performancestartsat" or "sentat")
                {
                    if (property.Value.ValueKind != JsonValueKind.String ||
                        !Regex.IsMatch(property.Value.GetString()!, @"(?:Z|[+-]\d{2}:\d{2})$"))
                        throw new InvalidDataException("Every instant must have an explicit UTC offset.");
                }
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CheckDuplicates(child);
    }

    public static ComparisonReport Compare(RiffRosterDbContext db, PreparedImport source)
    {
        db.ChangeTracker.Clear();
        // Stable snapshot across all tables while the comparison is read.
        using var transaction = db.Database.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);
        if (db.Database.GetPendingMigrations().Any() || !db.Set<ImportReceipt>().Any())
            throw new InvalidOperationException("Comparison requires a migrated and imported database.");
        var current = StateAudit.Report(StateProjection.Read(db));
        var report = Comparison(source.Report, current);
        transaction.Commit();
        return report;
    }
    public static ComparisonReport Comparison(StateReport source, StateReport database) => new(
        source.CanonicalSha256 == database.CanonicalSha256 && source.Counts.Count == database.Counts.Count && source.Counts.All(x => database.Counts.GetValueOrDefault(x.Key, -1) == x.Value), source, database);

    public static ComparisonReport Import(RiffRosterDbContext db, PreparedImport source)
    {
        // Callers may construct PreparedImport themselves; validate again before opening a transaction.
        StateAudit.Validate(source.State);
        if (source.Report.CanonicalSha256 != StateAudit.Report(source.State).CanonicalSha256)
            throw new InvalidDataException("Source changed after validation.");
        using var transaction = db.Database.BeginTransaction();
        db.Database.ExecuteSqlRaw($"SELECT pg_advisory_xact_lock({PostgresStatePersistence.StateLock})");
        if (db.Database.GetPendingMigrations().Any()) throw new InvalidOperationException("Apply the schema migration first.");
        var receipts = db.Set<ImportReceipt>().ToList();
        if (receipts.Count != 0)
        {
            if (receipts.Count != 1 || receipts[0].SourceSha256 != source.SourceSha256)
                throw new InvalidOperationException("Import destination is not empty.");
            var repeated = Comparison(source.Report, StateAudit.Report(StateProjection.Read(db)));
            if (!repeated.Matches) throw new InvalidOperationException("Imported database has changed; refusing overwrite.");
            transaction.Commit();
            return repeated;
        }
        if (db.Set<EventRow>().Any() || db.Set<DayRow>().Any() || db.Set<StageRow>().Any() || db.Set<PerformanceRow>().Any() ||
            db.Set<AccountRow>().Any() || db.Set<SelectionRow>().Any() || db.Set<GroupRow>().Any() || db.Set<MembershipRow>().Any() || db.Set<SubscriptionRow>().Any() || db.Set<DeliveryRow>().Any())
            throw new InvalidOperationException("Import destination is not empty.");
        StateProjection.Write(db, source.State);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        var comparison = Comparison(source.Report, StateAudit.Report(StateProjection.Read(db)));
        if (!comparison.Matches) throw new InvalidDataException("Import comparison failed; transaction rolled back.");
        db.Add(new ImportReceipt
        {
            Id = StableImportId.For("import", source.SourceSha256),
            SourceSha256 = source.SourceSha256,
            CanonicalSha256 = source.Report.CanonicalSha256,
            SourceDataVersion = source.SourceVersion,
            ImportedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
        transaction.Commit();
        return comparison;
    }
}
