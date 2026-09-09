using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace RiffRoster;

public sealed record DatabaseBackupManifest(int FormatVersion, string DatabaseIdentityHash, string DumpSha256, StateReport State, string[] Migrations);

public static class PostgresBackup
{
    public static string CreateDaily(string connection, string directory, string? binaryDirectory = null)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"riffroster-{DateTime.UtcNow:yyyyMMdd}.dump");
        var manifestPath = target + ".json";
        var config = new NpgsqlConnectionStringBuilder(connection);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{config.Host}|{config.Port}|{config.Database}")));
        if (File.Exists(target))
        {
            var existing = ReadManifest(target);
            if (existing.DatabaseIdentityHash != identity) throw new InvalidDataException("Backup directory belongs to a different database.");
            return target;
        }
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var db = RiffRosterDbContext.Connect(connection);
            using var transaction = db.Database.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);
            if (db.Database.GetPendingMigrations().Any() || !db.Set<ImportReceipt>().Any())
                throw new InvalidOperationException("Backup requires a migrated and imported database.");
            using var snapshotCommand = db.Database.GetDbConnection().CreateCommand();
            snapshotCommand.Transaction = transaction.GetDbTransaction();
            snapshotCommand.CommandText = "SELECT pg_export_snapshot()";
            var snapshot = (string)snapshotCommand.ExecuteScalar()!;
            var report = StateAudit.Report(StateProjection.Read(db));
            var migrations = db.Database.GetAppliedMigrations().ToArray();
            Run("pg_dump", connection, binaryDirectory, "--format=custom", "--no-owner", "--no-acl", "--snapshot=" + snapshot, "--file=" + temporary);
            transaction.Commit();
            var manifest = new DatabaseBackupManifest(1, identity, HashFile(temporary), report, migrations);
            // Exclusive, atomic rename. Never truncate a valid prior backup.
            File.Move(temporary, target, overwrite: false);
            using var output = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, manifest);
            output.Flush(true);
            return target;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static StateReport RestoreDrill(string connection, string dump, string? binaryDirectory = null)
    {
        var expected = ReadManifest(dump);
        var config = new NpgsqlConnectionStringBuilder(connection);
        var sourceDatabase = config.Database;
        config.Database = "postgres";
        using var admin = new NpgsqlConnection(config.ConnectionString);
        admin.Open();
        var name = "riff_restore_" + Guid.NewGuid().ToString("N");
        if (name == sourceDatabase) throw new InvalidOperationException("Restore database must be isolated.");
        using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) create.ExecuteNonQuery();
        config.Database = name;
        try
        {
            Run("pg_restore", config.ConnectionString, binaryDirectory, "--no-owner", "--no-acl", "--exit-on-error", "--dbname=" + name, Path.GetFullPath(dump));
            using var restored = RiffRosterDbContext.Connect(config.ConnectionString);
            var report = StateAudit.Report(StateProjection.Read(restored));
            if (!JsonImport.Comparison(expected.State, report).Matches ||
                !expected.Migrations.SequenceEqual(restored.Database.GetAppliedMigrations()) || !restored.Set<ImportReceipt>().Any())
                throw new InvalidDataException("Restored database failed content or schema verification.");
            return report;
        }
        finally
        {
            // Only the fresh name created above is dropped; never a user-specified database.
            using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            drop.ExecuteNonQuery();
        }
    }

    private static DatabaseBackupManifest ReadManifest(string dump)
    {
        var result = JsonSerializer.Deserialize<DatabaseBackupManifest>(File.ReadAllBytes(dump + ".json"))
            ?? throw new InvalidDataException("Backup manifest is missing.");
        if (result.FormatVersion != 1 || HashFile(dump) != result.DumpSha256)
            throw new InvalidDataException("Backup digest does not match its manifest.");
        return result;
    }
    private static string HashFile(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static void Run(string executable, string connection, string? binaryDirectory, params string[] arguments)
    {
        var config = new NpgsqlConnectionStringBuilder(connection);
        var filename = executable + (OperatingSystem.IsWindows() ? ".exe" : "");
        var start = new ProcessStartInfo(string.IsNullOrWhiteSpace(binaryDirectory) ? filename : Path.Combine(binaryDirectory, filename))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // No credentials in command line, process logs or the manifest.
        start.Environment["PGHOST"] = config.Host;
        start.Environment["PGPORT"] = config.Port.ToString();
        start.Environment["PGUSER"] = config.Username;
        start.Environment["PGPASSWORD"] = config.Password ?? "";
        start.Environment["PGDATABASE"] = config.Database;
        start.Environment["PGSSLMODE"] = config.SslMode switch
        {
            SslMode.VerifyFull => "verify-full",
            SslMode.VerifyCA => "verify-ca",
            _ => config.SslMode.ToString().ToLowerInvariant()
        };
        if (!string.IsNullOrWhiteSpace(config.RootCertificate)) start.Environment["PGSSLROOTCERT"] = config.RootCertificate;
        start.Environment["PGCONNECT_TIMEOUT"] = "15";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PostgreSQL client could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(300_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("PostgreSQL backup/restore exceeded its time limit.");
        }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0) throw new InvalidOperationException("PostgreSQL backup/restore failed. Check client/server versions, credentials and disk space.");
    }
}
