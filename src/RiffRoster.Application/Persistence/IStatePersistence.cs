namespace RiffRoster;

// Compatibility unit of work. Replace whole-state reads with feature queries incrementally.
public interface IStatePersistence
{
    IStateSession Open();
    bool CreateBackup();
}

public interface IStateSession : IDisposable
{
    AppState State { get; }
    void Save(AppState state);
}
