using Npgsql;
using Testcontainers.PostgreSql;

namespace RiffRoster.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _connection = "";
    private readonly List<string> _ownedDatabases = [];
    public async Task InitializeAsync()
    {
        var local = Environment.GetEnvironmentVariable("RIFFROSTER_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(local))
        {
            _container = new PostgreSqlBuilder("postgres:17.11").Build();
            await _container.StartAsync();
            _connection = _container.GetConnectionString();
        }
        else
        {
            var config = new NpgsqlConnectionStringBuilder(local);
            if (config.Host is not ("127.0.0.1" or "localhost" or "::1"))
                throw new InvalidOperationException("Test override must target a disposable localhost cluster.");
            _connection = local;
        }
    }
    public async Task<string> NewDatabase()
    {
        var name = "riff_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_connection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
        await command.ExecuteNonQueryAsync();
        _ownedDatabases.Add(name);
        return new NpgsqlConnectionStringBuilder(_connection) { Database = name }.ConnectionString;
    }
    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if (_container is not null) await _container.DisposeAsync();
        else
        {
            await using var admin = new NpgsqlConnection(_connection);
            await admin.OpenAsync();
            foreach (var name in _ownedDatabases)
            {
                // Only names generated and registered by this fixture; no user-supplied SQL identifiers.
                await using var command = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
