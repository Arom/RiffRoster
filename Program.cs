using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using RiffRoster;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
if (builder.Environment.IsDevelopment()) builder.Logging.AddDebug();
var security = SecuritySettings.FromConfiguration(builder.Configuration, builder.Environment);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("RiffRoster");
var keysPath = builder.Configuration["RIFFROSTER_KEYS_PATH"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    var resolvedKeysPath = DeploymentPaths.Resolve(keysPath, builder.Environment.ContentRootPath);
    Directory.CreateDirectory(resolvedKeysPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(resolvedKeysPath));
}

builder.Services.AddSingleton(security);
builder.Services.AddSingleton<DataStore>();
builder.Services.AddHostedService<BackupService>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "RiffRoster.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = async context =>
        {
            var currentSecurity = context.HttpContext.RequestServices.GetRequiredService<SecuritySettings>();
            var sessionStamp = context.Principal?.FindFirstValue("riffroster:security_stamp");
            if (!string.Equals(sessionStamp, currentSecurity.SessionStamp, StringComparison.Ordinal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireClaim(ClaimTypes.Role, "admin"));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

api.MapGet("/health", (DataStore store) => Results.Ok(store.GetHealthSnapshot()))
    .AllowAnonymous();

api.MapPost("/session", async (LoginRequest request, DataStore store, SecuritySettings settings, HttpContext context) =>
{
    var username = request.Username?.Trim();
    if (string.IsNullOrWhiteSpace(username) || username.Length is < 2 or > 24)
        return Results.BadRequest(new { message = "Use a username between 2 and 24 characters." });

    if (!settings.TryAuthorize(request.AccessCode, out var isAdmin))
        return Results.Unauthorized();

    var user = store.EnsureUser(username);
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, isAdmin ? "admin" : "member"),
        new("riffroster:security_stamp", settings.SessionStamp)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });

    return Results.Ok(new { user, isAdmin });
}).AllowAnonymous();

api.MapPost("/session/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization();

api.MapGet("/state", (HttpContext context, DataStore store) =>
{
    var username = context.User.Identity!.Name!;
    return Results.Ok(store.GetSnapshot(username, context.User.IsInRole("admin")));
}).RequireAuthorization();

api.MapPost("/selections/toggle", (ToggleSelectionRequest request, HttpContext context, DataStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.PerformanceId))
        return Results.BadRequest(new { message = "A performance is required." });

    var result = store.ToggleSelection(context.User.Identity!.Name!, request.PerformanceId);
    return result is null
        ? Results.NotFound(new { message = "That performance no longer exists." })
        : Results.Ok(result);
}).RequireAuthorization();

api.MapPost("/events", (CreateEventRequest request, DataStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Location))
        return Results.BadRequest(new { message = "Name and location are required." });
    if (request.EndDate < request.StartDate || request.EndDate.DayNumber - request.StartDate.DayNumber > 14)
        return Results.BadRequest(new { message = "Choose an end date within 14 days of the start date." });

    return Results.Ok(store.CreateEvent(request));
}).RequireAuthorization("Admin");

api.MapPost("/events/{eventId}/performances", (string eventId, CreatePerformanceRequest request, DataStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.Artist) || string.IsNullOrWhiteSpace(request.Stage))
        return Results.BadRequest(new { message = "Artist and stage are required." });
    if (request.EndsAt <= request.StartsAt)
        return Results.BadRequest(new { message = "The end time must be after the start time." });

    var performance = store.AddPerformance(eventId, request);
    return performance is null
        ? Results.NotFound(new { message = "Running order not found, or the performance falls outside its dates." })
        : Results.Ok(performance);
}).RequireAuthorization("Admin");

app.MapFallbackToFile("index.html");
app.Run();

namespace RiffRoster
{
    public sealed record LoginRequest(string? Username, string? AccessCode);
    public sealed record ToggleSelectionRequest(string PerformanceId);
    public sealed record CreateEventRequest(string Name, string Location, DateOnly StartDate, DateOnly EndDate);
    public sealed record CreatePerformanceRequest(string Artist, string Stage, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

    public sealed class SecuritySettings(string inviteCode, string adminCode)
    {
        private readonly byte[] _inviteCodeHash = Hash(inviteCode);
        private readonly byte[] _adminCodeHash = Hash(adminCode);
        public string SessionStamp { get; } = Convert.ToHexString(Hash($"{inviteCode}\n{adminCode}"));

        public static SecuritySettings FromConfiguration(IConfiguration configuration, IWebHostEnvironment environment)
        {
            var inviteCode = configuration["RIFFROSTER_INVITE_CODE"] ?? configuration["Security:InviteCode"];
            var adminCode = configuration["RIFFROSTER_ADMIN_CODE"] ?? configuration["Security:AdminCode"];
            if (string.IsNullOrWhiteSpace(inviteCode) || string.IsNullOrWhiteSpace(adminCode))
                throw new InvalidOperationException(
                    "Set RIFFROSTER_INVITE_CODE and RIFFROSTER_ADMIN_CODE before starting RiffRoster.");
            if (inviteCode.Equals(adminCode, StringComparison.Ordinal))
                throw new InvalidOperationException("The member invite code and administrator code must be different.");
            if (!environment.IsDevelopment() && (inviteCode.Length < 16 || adminCode.Length < 16))
                throw new InvalidOperationException("Production access codes must contain at least 16 characters.");

            return new SecuritySettings(inviteCode, adminCode);
        }

        public bool TryAuthorize(string? candidate, out bool isAdmin)
        {
            var candidateHash = Hash(candidate ?? string.Empty);
            isAdmin = CryptographicOperations.FixedTimeEquals(candidateHash, _adminCodeHash);
            return isAdmin || CryptographicOperations.FixedTimeEquals(candidateHash, _inviteCodeHash);
        }

        private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }

    public static class DeploymentPaths
    {
        public static string Resolve(string path, string contentRoot) =>
            Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path));
    }

    public sealed class AppState
    {
        public int DataVersion { get; set; }
        public List<FestivalEvent> Events { get; set; } = [];
        public List<AppUser> Users { get; set; } = [];
        public List<UserSelection> Selections { get; set; } = [];
    }

    public sealed record FestivalEvent(string Id, string Name, string Location, DateOnly StartDate, DateOnly EndDate, List<FestivalDay> Days);
    public sealed record FestivalDay(string Id, DateOnly Date, string Label, List<Performance> Performances);
    public sealed record Performance(string Id, string Artist, string Stage, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
    public sealed record AppUser(string Username, string Color, DateTimeOffset JoinedAt);
    public sealed record UserSelection(string Username, string PerformanceId);

    public sealed class DataStore
    {
        private const int CurrentDataVersion = 3;
        private readonly object _gate = new();
        private readonly string _path;
        private readonly string _backupDirectory;
        private readonly ILogger<DataStore> _logger;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        private AppState _state = new();

        public DataStore(IWebHostEnvironment environment, IConfiguration configuration, ILogger<DataStore> logger)
        {
            _logger = logger;
            var configuredPath = configuration["RIFFROSTER_DATA_PATH"] ?? configuration["Storage:DataPath"];
            _path = DeploymentPaths.Resolve(
                string.IsNullOrWhiteSpace(configuredPath) ? Path.Combine("App_Data", "store.json") : configuredPath,
                environment.ContentRootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            var configuredBackupDirectory = configuration["RIFFROSTER_BACKUP_DIR"] ?? configuration["Storage:BackupDirectory"];
            _backupDirectory = string.IsNullOrWhiteSpace(configuredBackupDirectory)
                ? Path.Combine(Path.GetDirectoryName(_path)!, "backups")
                : DeploymentPaths.Resolve(configuredBackupDirectory, environment.ContentRootPath);
            Directory.CreateDirectory(_backupDirectory);
            _state = LoadOrSeed();
        }

        public AppUser EnsureUser(string username)
        {
            lock (_gate)
            {
                var existing = _state.Users.FirstOrDefault(user => user.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                if (existing is not null) return existing;

                var user = new AppUser(username, UserColor(username), DateTimeOffset.UtcNow);
                _state.Users.Add(user);
                Save();
                return user;
            }
        }

        public object GetSnapshot(string username, bool isAdmin)
        {
            lock (_gate)
            {
                var canonicalName = EnsureUser(username).Username;
                return new
                {
                    currentUser = canonicalName,
                    isAdmin,
                    events = _state.Events,
                    users = _state.Users.OrderBy(user => user.JoinedAt),
                    selections = _state.Selections
                };
            }
        }

        public object GetHealthSnapshot()
        {
            lock (_gate)
                return new
                {
                    status = "ok",
                    events = _state.Events.Count,
                    users = _state.Users.Count,
                    selections = _state.Selections.Count,
                    checkedAt = DateTimeOffset.UtcNow
                };
        }

        public object? ToggleSelection(string username, string performanceId)
        {
            lock (_gate)
            {
                var performanceExists = _state.Events.SelectMany(e => e.Days).SelectMany(d => d.Performances).Any(p => p.Id == performanceId);
                if (!performanceExists) return null;

                var user = EnsureUser(username);
                var existing = _state.Selections.FirstOrDefault(selection =>
                    selection.PerformanceId == performanceId && selection.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase));
                var selected = existing is null;
                if (existing is null)
                    _state.Selections.Add(new UserSelection(user.Username, performanceId));
                else
                    _state.Selections.Remove(existing);

                Save();
                return new { selected, performanceId, username = user.Username };
            }
        }

        public FestivalEvent CreateEvent(CreateEventRequest request)
        {
            lock (_gate)
            {
                var eventId = $"event-{Guid.NewGuid():N}";
                var days = new List<FestivalDay>();
                for (var date = request.StartDate; date <= request.EndDate; date = date.AddDays(1))
                    days.Add(new FestivalDay($"{eventId}-{date:yyyyMMdd}", date, date.ToString("dddd"), []));

                var festivalEvent = new FestivalEvent(eventId, request.Name.Trim(), request.Location.Trim(), request.StartDate, request.EndDate, days);
                _state.Events.Add(festivalEvent);
                Save();
                return festivalEvent;
            }
        }

        public Performance? AddPerformance(string eventId, CreatePerformanceRequest request)
        {
            lock (_gate)
            {
                var festivalEvent = _state.Events.FirstOrDefault(item => item.Id == eventId);
                if (festivalEvent is null) return null;

                var localDateTime = request.StartsAt.DateTime;
                var localDate = DateOnly.FromDateTime(localDateTime);
                var scheduleDate = localDateTime.TimeOfDay < TimeSpan.FromHours(6) ? localDate.AddDays(-1) : localDate;
                var day = festivalEvent.Days.FirstOrDefault(item => item.Date == scheduleDate);
                if (day is null) return null;

                var performance = new Performance($"perf-{Guid.NewGuid():N}", request.Artist.Trim(), request.Stage.Trim(), request.StartsAt, request.EndsAt);
                day.Performances.Add(performance);
                day.Performances.Sort((a, b) => a.StartsAt.CompareTo(b.StartsAt));
                Save();
                return performance;
            }
        }

        public bool CreateBackup()
        {
            lock (_gate)
            {
                if (!File.Exists(_path)) return false;
                var backupPath = Path.Combine(_backupDirectory, $"store-{DateTime.UtcNow:yyyyMMdd}.json");
                if (File.Exists(backupPath)) return false;

                File.Copy(_path, backupPath);
                foreach (var oldBackup in Directory.EnumerateFiles(_backupDirectory, "store-*.json")
                             .OrderByDescending(Path.GetFileName)
                             .Skip(14))
                    File.Delete(oldBackup);
                return true;
            }
        }

        private AppState LoadOrSeed()
        {
            if (File.Exists(_path))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<AppState>(File.ReadAllText(_path), _jsonOptions);
                    if (loaded is not null)
                    {
                        ApplyMigrations(loaded);

                        return loaded;
                    }
                }
                catch (JsonException exception)
                {
                    var invalidBackup = Path.Combine(_backupDirectory, $"invalid-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
                    File.Copy(_path, invalidBackup);
                    _logger.LogError(exception, "The data file was invalid and was preserved as {BackupFile}.", invalidBackup);
                }
            }

            var seeded = SeedData.Create();
            _state = seeded;
            Save();
            return seeded;
        }

        private void ApplyMigrations(AppState loaded)
        {
            if (loaded.DataVersion >= CurrentDataVersion) return;

            _state = loaded;
            if (loaded.DataVersion < 2)
            {
                var removedUsers = loaded.Users.Count;
                var removedSelections = loaded.Selections.Count;
                loaded.Users.Clear();
                loaded.Selections.Clear();
                loaded.DataVersion = 2;
                _logger.LogInformation(
                    "Updated data to version 2; removed {UserCount} users and {SelectionCount} selections.",
                    removedUsers,
                    removedSelections);
            }

            if (loaded.DataVersion < 3)
            {
                var eventIndex = loaded.Events.FindIndex(item => item.Id == "brutal-assault-2026");
                if (eventIndex >= 0)
                {
                    var festivalEvent = loaded.Events[eventIndex];
                    if (festivalEvent.Days.All(day => day.Date != new DateOnly(2026, 8, 4)))
                        festivalEvent.Days.Add(SeedData.CreateTuesday());

                    festivalEvent.Days.Sort((left, right) => left.Date.CompareTo(right.Date));
                    loaded.Events[eventIndex] = festivalEvent with { StartDate = new DateOnly(2026, 8, 4) };
                }

                loaded.DataVersion = 3;
                _logger.LogInformation("Updated data to version 3; added the Brutal Assault Tuesday warm-up schedule.");
            }

            Save();
        }

        private void Save()
        {
            var tempPath = $"{_path}.tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_state, _jsonOptions));
            File.Move(tempPath, _path, true);
        }

        private static string UserColor(string username)
        {
            string[] colors = ["#FFB000", "#FF6257", "#70D6A7", "#8FB8FF", "#D59CFF", "#F47FC4", "#7DD3FC"];
            var hash = username.Aggregate(17, (current, character) => unchecked(current * 31 + character));
            return colors[(hash & int.MaxValue) % colors.Length];
        }
    }

    public sealed class BackupService(DataStore store, ILogger<BackupService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (store.CreateBackup())
                        logger.LogInformation("Created the daily RiffRoster data backup.");
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Could not create the daily RiffRoster data backup.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
