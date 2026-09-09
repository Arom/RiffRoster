using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RiffRoster;

// Connection credentials are environment-only, never command-line arguments or report content.
try
{
    if (args.Length == 0 || args[0] is not ("dry-run" or "migrate" or "import" or "compare" or "report" or "export" or "backup" or "backup-loop" or "restore-drill"))
        throw new ArgumentException("Use dry-run SOURCE, migrate, import SOURCE, compare SOURCE, report, export NEW-FILE, backup DIRECTORY, backup-loop DIRECTORY, or restore-drill DUMP.");
    var command = args[0];
    var requiresFile = command is "dry-run" or "import" or "compare" or "export" or "backup" or "backup-loop" or "restore-drill";
    if (args.Length != (requiresFile ? 2 : 1)) throw new ArgumentException("Incorrect argument count.");
    var source = command is "dry-run" or "import" or "compare" ? JsonImport.ReadSource(args[1]) : null;
    if (command == "dry-run")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { source!.SourceVersion, source.SourceSha256, source.Report, orphanCount = 0, mappingVersion = 1 }));
        return 0;
    }
    var connection = Environment.GetEnvironmentVariable("RIFFROSTER_POSTGRES_CONNECTION");
    if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("PostgreSQL connection is required.");
    var binaries = Environment.GetEnvironmentVariable("RIFFROSTER_PG_BIN");
    if (command == "restore-drill")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { restored = true, report = PostgresBackup.RestoreDrill(connection, args[1], binaries) }));
        return 0;
    }
    if (command is "backup" or "backup-loop")
    {
        do
        {
            // Supervised loop: failure exits nonzero for the service manager to alert/restart.
            PostgresBackup.CreateDaily(connection, args[1], binaries);
            Console.WriteLine(JsonSerializer.Serialize(new { backupVerified = true, checkedAt = DateTimeOffset.UtcNow }));
            if (command == "backup") return 0;
            await Task.Delay(TimeSpan.FromHours(6));
        } while (true);
    }
    using var db = RiffRosterDbContext.Connect(connection);
    if (command == "migrate")
    {
        db.Database.Migrate();
        Console.WriteLine(JsonSerializer.Serialize(new { applied = db.Database.GetAppliedMigrations() }));
    }
    else if (command is "import" or "compare")
    {
        var result = command == "import" ? JsonImport.Import(db, source!) : JsonImport.Compare(db, source!);
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Matches ? 0 : 2;
    }
    else
    {
        using var transaction = db.Database.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);
        if (db.Database.GetPendingMigrations().Any() || !db.Set<ImportReceipt>().Any())
            throw new InvalidOperationException("Report/export requires a migrated and imported database.");
        var state = StateProjection.Read(db);
        var report = StateAudit.Report(state);
        if (command == "export")
        {
            using var target = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(target, state, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            target.Flush(true);
        }
        transaction.Commit();
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
    return 0;
}
catch (Exception exception)
{
    // Exception messages can contain JSON fragments, SQL row details or credentials.
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        error = "migration_failed",
        category = exception.GetType().Name,
        guidance = "Check arguments, source schema/references, connection and empty destination. No raw data is logged. Never retry by clearing live tables."
    }));
    return 1;
}
