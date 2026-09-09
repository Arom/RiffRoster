using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RiffRoster;

public sealed class JsonStatePersistence : IStatePersistence
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _backupDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private AppState _state;

    public JsonStatePersistence(IWebHostEnvironment environment, IConfiguration configuration, ILogger<DataStore> logger)
    {
        var configuredPath = configuration["RIFFROSTER_DATA_PATH"] ?? configuration["Storage:DataPath"];
        _path = DeploymentPaths.Resolve(string.IsNullOrWhiteSpace(configuredPath) ? Path.Combine("App_Data", "store.json") : configuredPath, environment.ContentRootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var configuredBackup = configuration["RIFFROSTER_BACKUP_DIR"] ?? configuration["Storage:BackupDirectory"];
        _backupDirectory = string.IsNullOrWhiteSpace(configuredBackup) ? Path.Combine(Path.GetDirectoryName(_path)!, "backups") : DeploymentPaths.Resolve(configuredBackup, environment.ContentRootPath);
        Directory.CreateDirectory(_backupDirectory);
        if (!File.Exists(_path))
        {
            // Bootstrap only when no store exists. Invalid existing data never reaches here.
            _state = SeedData.Create();
            WriteAtomically(_path, JsonSerializer.SerializeToUtf8Bytes(_state, _jsonOptions), overwrite: false);
            return;
        }
        try
        {
            var source = JsonImport.ReadSource(_path);
            _state = source.State;
            if (source.SourceVersion < 7)
            {
                File.Copy(_path, Path.Combine(_backupDirectory, $"before-v{source.SourceVersion}-upgrade-{Guid.NewGuid():N}.json"), overwrite: false);
                WriteAtomically(_path, JsonSerializer.SerializeToUtf8Bytes(_state, _jsonOptions), overwrite: true);
            }
        }
        catch
        {
            // Do not log exception payloads, source rows or secrets.
            logger.LogError("The JSON store could not be loaded safely. Startup stopped; restore a verified backup before retrying.");
            throw new InvalidDataException("JSON store initialization failed. The source was not replaced with seed data.");
        }
    }

    public bool CreateBackup()
    {
        lock (_gate)
        {
            var backupPath = Path.Combine(_backupDirectory, $"store-{DateTime.UtcNow:yyyyMMdd}.json");
            if (File.Exists(backupPath))
            {
                JsonImport.ReadSource(backupPath);
                return false;
            }
            var bytes = File.ReadAllBytes(_path);
            JsonImport.Prepare(bytes);
            WriteAtomically(backupPath, bytes, overwrite: false);
            foreach (var old in Directory.EnumerateFiles(_backupDirectory, "store-*.json").OrderByDescending(Path.GetFileName).Skip(14))
                File.Delete(old);
            return true;
        }
    }

    private static void WriteAtomically(string target, byte[] bytes, bool overwrite)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public IStateSession Open() => new JsonSession(this);
    private sealed class JsonSession : IStateSession
    {
        private readonly JsonStatePersistence _owner;
        private bool _disposed;
        public AppState State { get; }
        public JsonSession(JsonStatePersistence owner)
        {
            _owner = owner;
            Monitor.Enter(owner._gate);
            try { State = JsonSerializer.Deserialize<AppState>(JsonSerializer.SerializeToUtf8Bytes(owner._state, owner._jsonOptions), owner._jsonOptions)!; }
            catch { Monitor.Exit(owner._gate); throw; }
        }
        public void Save(AppState state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(state, _owner._jsonOptions);
            var validated = JsonImport.Prepare(bytes).State;
            WriteAtomically(_owner._path, bytes, overwrite: true);
            _owner._state = validated;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Monitor.Exit(_owner._gate);
        }
    }
}
