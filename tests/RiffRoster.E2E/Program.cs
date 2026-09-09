using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RiffRoster;
using Testcontainers.PostgreSql;

// Test-only host: always creates a fresh database; never accepts a destination database name.
PostgreSqlContainer? container = null;
var connection = Environment.GetEnvironmentVariable("RIFFROSTER_TEST_POSTGRES");
if (string.IsNullOrWhiteSpace(connection))
{
    container = new PostgreSqlBuilder("postgres:17.11").Build();
    await container.StartAsync();
    connection = container.GetConnectionString();
}
var config = new NpgsqlConnectionStringBuilder(connection);
if (config.Host is not ("127.0.0.1" or "localhost" or "::1"))
    throw new InvalidOperationException("Browser tests require a disposable localhost PostgreSQL cluster.");
var name = "riff_e2e_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(connection);
await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
config.Database = name;
try
{
    using (var db = RiffRosterDbContext.Connect(config.ConnectionString))
    {
        db.Database.Migrate();
        var fixture = JsonImport.Prepare(JsonSerializer.SerializeToUtf8Bytes(SeedData.Create(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        JsonImport.Import(db, fixture);
    }
    Environment.SetEnvironmentVariable("RIFFROSTER_STORAGE", "Postgres");
    Environment.SetEnvironmentVariable("RIFFROSTER_POSTGRES_CONNECTION", config.ConnectionString);
    ApiHost.Run(args);
}
finally
{
    await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
    await drop.ExecuteNonQueryAsync();
    if (container is not null) await container.DisposeAsync();
}
