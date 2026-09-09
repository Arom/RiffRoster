using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RiffRoster;

public sealed class PostgresStatePersistence(string connection) : IStatePersistence
{
    // Phase-1 compatibility lock; transaction lifetime covers both read and write.
    public const long StateLock = 731940612;
    public IStateSession Open() => new Session(connection);
    public bool CreateBackup() => false; // pg_dump job backs up PostgreSQL; never write a JSON substitute.

    private sealed class Session : IStateSession
    {
        private readonly RiffRosterDbContext _db;
        private readonly IDbContextTransaction _transaction;
        public AppState State { get; }
        public Session(string connection)
        {
            _db = RiffRosterDbContext.Connect(connection);
            try
            {
                _transaction = _db.Database.BeginTransaction();
                _db.Database.ExecuteSqlRaw($"SELECT pg_advisory_xact_lock({StateLock})");
                if (_db.Database.GetPendingMigrations().Any() || !_db.Set<ImportReceipt>().Any())
                    throw new InvalidOperationException("PostgreSQL requires an explicit migration and verified import before serving requests.");
                State = StateProjection.Read(_db);
            }
            catch { _db.Dispose(); throw; }
        }
        public void Save(AppState state)
        {
            StateProjection.Write(_db, state);
            _db.SaveChanges();
            _transaction.Commit();
        }
        public void Dispose() { _transaction.Dispose(); _db.Dispose(); }
    }
}
