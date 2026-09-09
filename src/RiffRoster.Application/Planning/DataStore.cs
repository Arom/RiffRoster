using System.Security.Cryptography;
using System.Text;
namespace RiffRoster;

public sealed partial class DataStore(IStatePersistence persistence)
{
    private readonly object _gate = new();
    private AppState _state = new();
    private IStateSession? _session;
    private IDisposable EnterState()
    {
        Monitor.Enter(_gate);
        try
        {
            _session = persistence.Open();
            _state = _session.State;
            return new Lease(this);
        }
        catch { Monitor.Exit(_gate); throw; }
    }
    private sealed class Lease(DataStore owner) : IDisposable
    {
        public void Dispose()
        {
            try { owner._session?.Dispose(); owner._session = null; }
            finally { Monitor.Exit(owner._gate); }
        }
    }
    private void Save() => _session!.Save(_state);
    public bool CreateBackup() { lock (_gate) return persistence.CreateBackup(); }
}
