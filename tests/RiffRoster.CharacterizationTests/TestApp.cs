using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RiffRoster;

namespace RiffRoster.CharacterizationTests;

public sealed class TestApp : WebApplicationFactory<Program>
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "riffroster-tests", Guid.NewGuid().ToString("N"));
    public string StorePath => Path.Combine(DirectoryPath, "store.json");
    public string BackupPath => Path.Combine(DirectoryPath, "backups");
    public DataStore Store => Services.GetRequiredService<DataStore>();
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TestApp(string? fixture = null)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (fixture is not null)
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture), StorePath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("RIFFROSTER_STORAGE", "Json");
        builder.UseSetting("RIFFROSTER_READ_ONLY", "false");
        builder.UseSetting("RIFFROSTER_INVITE_CODE", "friends-local");
        builder.UseSetting("RIFFROSTER_ADMIN_CODE", "admin-local-only");
        builder.UseSetting("RIFFROSTER_KEYS_PATH", Path.Combine(DirectoryPath, "keys"));
        builder.UseSetting("RIFFROSTER_VAPID_PUBLIC_KEY", "");
        builder.UseSetting("RIFFROSTER_VAPID_PRIVATE_KEY", "");
        builder.UseSetting("RIFFROSTER_VAPID_SUBJECT", "");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RIFFROSTER_DATA_PATH"] = StorePath,
            ["RIFFROSTER_BACKUP_DIR"] = BackupPath
        }));
        builder.ConfigureServices(services =>
        {
            // No timed backups or real push delivery in tests. Exercise these explicitly.
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                         (d.ImplementationType == typeof(BackupService) || d.ImplementationType == typeof(PushReminderService))).ToList())
                services.Remove(descriptor);
        });
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task<HttpClient> Login(string name = "Alice", bool admin = false)
    {
        var client = Client();
        var response = await client.PostAsJsonAsync("/api/session", new
        {
            loginName = $"private-{name}",
            displayName = name,
            password = "fixture-password-only",
            accessCode = admin ? "admin-local-only" : "friends-local"
        });
        response.EnsureSuccessStatusCode();
        return client;
    }

    public AppState ReadState() => JsonSerializer.Deserialize<AppState>(File.ReadAllText(StorePath), Json)!;
    public static JsonElement Element(object? value) => JsonSerializer.SerializeToElement(value, Json);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
    }
}
