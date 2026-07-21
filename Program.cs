using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RiffRoster;
using WebPush;

if (args.Any(argument => argument.Equals("--generate-vapid", StringComparison.OrdinalIgnoreCase)))
{
    var generatedKeys = VapidHelper.GenerateVapidKeys();
    Console.WriteLine(JsonSerializer.Serialize(new { publicKey = generatedKeys.PublicKey, privateKey = generatedKeys.PrivateKey }));
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
if (builder.Environment.IsDevelopment()) builder.Logging.AddDebug();
var security = SecuritySettings.FromConfiguration(builder.Configuration, builder.Environment);
var pushSettings = PushSettings.FromConfiguration(builder.Configuration);

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
builder.Services.AddSingleton(pushSettings);
builder.Services.AddSingleton<DataStore>();
builder.Services.AddSingleton<PushNotificationSender>();
builder.Services.AddHostedService<BackupService>();
builder.Services.AddHostedService<PushReminderService>();
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
            var authenticationVersion = context.Principal?.FindFirstValue("riffroster:auth_version");
            if (!string.Equals(sessionStamp, currentSecurity.SessionStamp, StringComparison.Ordinal) ||
                !string.Equals(authenticationVersion, "2", StringComparison.Ordinal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireClaim(ClaimTypes.Role, "admin"));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("account-actions", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 12,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

api.MapGet("/health", (DataStore store, PushSettings settings) => Results.Ok(store.GetHealthSnapshot(settings.Enabled)))
    .AllowAnonymous();

api.MapPost("/session", async (LoginRequest request, DataStore store, SecuritySettings settings, HttpContext context) =>
{
    if (!settings.TryAuthorize(request.AccessCode, out var isAdmin))
        return Results.Json(new { message = "Those login details are not valid." }, statusCode: StatusCodes.Status401Unauthorized);

    var validationMessage = AccountCredentials.Validate(request);
    if (validationMessage is not null)
        return Results.BadRequest(new { message = validationMessage });

    var signIn = store.SignInOrRegister(request.LoginName!, request.DisplayName!, request.Password!);
    if (!signIn.Succeeded)
        return Results.Json(new { message = signIn.Message }, statusCode: signIn.StatusCode);

    var user = signIn.User!;
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, isAdmin ? "admin" : "member"),
        new("riffroster:auth_version", "2"),
        new("riffroster:security_stamp", settings.SessionStamp)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });

    return Results.Ok(new { user = DataStore.PublicUser(user), isAdmin, created = signIn.Created, claimed = signIn.Claimed });
}).AllowAnonymous().RequireRateLimiting("account-actions");

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

api.MapPut("/account/last-event", (SetLastSelectedEventRequest request, HttpContext context, DataStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.EventId))
        return Results.BadRequest(new { message = "A festival is required." });

    return store.SetLastSelectedEvent(context.User.Identity!.Name!, request.EventId)
        ? Results.Ok(new { eventId = request.EventId })
        : Results.NotFound(new { message = "That festival no longer exists." });
}).RequireAuthorization();

api.MapPost("/groups", (CreateGroupRequest request, HttpContext context, DataStore store) =>
{
    var name = request.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length is < 2 or > 48)
        return Results.BadRequest(new { message = "Use a group name between 2 and 48 characters." });

    return Results.Ok(store.CreateGroup(context.User.Identity!.Name!, name));
}).RequireAuthorization();

api.MapPost("/groups/join", (JoinGroupRequest request, HttpContext context, DataStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.JoinCode))
        return Results.BadRequest(new { message = "Enter a group join code." });

    var result = store.JoinGroup(context.User.Identity!.Name!, request.JoinCode);
    return result is null
        ? Results.NotFound(new { message = "That group join code is not valid." })
        : Results.Ok(result);
}).RequireAuthorization().RequireRateLimiting("account-actions");

api.MapPost("/groups/{groupId}/leave", (string groupId, HttpContext context, DataStore store) =>
{
    var result = store.LeaveGroup(context.User.Identity!.Name!, groupId);
    return result is null
        ? Results.NotFound(new { message = "You are not a member of that group." })
        : Results.Ok(result);
}).RequireAuthorization();

api.MapPost("/groups/{groupId}/join-code", (string groupId, HttpContext context, DataStore store) =>
{
    var result = store.RegenerateJoinCode(context.User.Identity!.Name!, groupId);
    return result is null
        ? Results.Forbid()
        : Results.Ok(result);
}).RequireAuthorization().RequireRateLimiting("account-actions");

api.MapGet("/push/public-key", (PushSettings settings) =>
    settings.Enabled
        ? Results.Ok(new { publicKey = settings.PublicKey })
        : Results.Json(new { message = "Push notifications are not configured yet." }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .RequireAuthorization();

api.MapPost("/push/subscriptions", (SavePushSubscriptionRequest request, HttpContext context, DataStore store, PushSettings settings) =>
{
    if (!settings.Enabled)
        return Results.Json(new { message = "Push notifications are not configured yet." }, statusCode: StatusCodes.Status503ServiceUnavailable);

    var validationMessage = PushSubscriptionValidator.Validate(request);
    return validationMessage is null
        ? Results.Ok(store.UpsertPushSubscription(context.User.Identity!.Name!, request))
        : Results.BadRequest(new { message = validationMessage });
}).RequireAuthorization();

api.MapDelete("/push/subscriptions", ([FromBody] DeletePushSubscriptionRequest request, HttpContext context, DataStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.Endpoint))
        return Results.BadRequest(new { message = "A push subscription endpoint is required." });

    store.RemovePushSubscription(context.User.Identity!.Name!, request.Endpoint);
    return Results.NoContent();
}).RequireAuthorization();

api.MapPost("/push/test", async (TestPushRequest request, HttpContext context, DataStore store, PushNotificationSender sender, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Endpoint))
        return Results.BadRequest(new { message = "A push subscription endpoint is required." });

    var device = store.GetPushDevice(context.User.Identity!.Name!, request.Endpoint);
    if (device is null)
        return Results.NotFound(new { message = "Enable reminders on this device first." });

    var payload = JsonSerializer.Serialize(new
    {
        title = "RiffRoster reminders are on",
        body = "This device is ready for concert reminders.",
        icon = "/icons/icon-192.png",
        badge = "/icons/badge-96.png",
        tag = "riffroster-test",
        data = new { url = "/" }
    });

    try
    {
        var outcome = await sender.SendAsync(device, payload, cancellationToken);
        if (outcome == PushSendOutcome.Expired)
        {
            store.RemovePushDevice(device.Id);
            return Results.Json(new { message = "This browser subscription expired. Enable reminders again." }, statusCode: StatusCodes.Status410Gone);
        }

        return Results.Ok(new { sent = true });
    }
    catch (Exception exception)
    {
        loggerFactory.CreateLogger("RiffRoster.PushTest")
            .LogWarning(exception, "A test push could not be sent for {Username}.", context.User.Identity!.Name!);
        return Results.Json(
            new { message = "The test notification could not be sent. Try enabling reminders again." },
            statusCode: StatusCodes.Status502BadGateway);
    }
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

api.MapDelete("/events/{eventId}", (string eventId, DataStore store) =>
{
    var result = store.DeleteEvent(eventId);
    return result is null
        ? Results.NotFound(new { message = "That festival no longer exists." })
        : Results.Ok(result);
}).RequireAuthorization("Admin");

api.MapPost("/events/{eventId}/performances/bulk", (string eventId, BulkCreatePerformancesRequest request, DataStore store) =>
{
    if (request.Performances is null || request.Performances.Count is < 1 or > 500)
        return Results.BadRequest(new { message = "Choose between 1 and 500 performances to import." });

    foreach (var performance in request.Performances)
    {
        if (string.IsNullOrWhiteSpace(performance.Artist) || string.IsNullOrWhiteSpace(performance.Stage))
            return Results.BadRequest(new { message = "Every performance needs an artist and stage." });
        if (performance.Artist.Length > 160 || performance.Stage.Length > 100)
            return Results.BadRequest(new { message = "An artist or stage name is too long." });
        if (performance.EndsAt <= performance.StartsAt)
            return Results.BadRequest(new { message = "Every end time must be after its start time." });
        if (performance.EndsAt - performance.StartsAt > TimeSpan.FromHours(24))
            return Results.BadRequest(new { message = "A performance cannot run for more than 24 hours." });
    }

    var result = store.AddPerformances(eventId, request.Performances);
    return result is null
        ? Results.NotFound(new { message = "Running order not found, or a performance falls outside its dates." })
        : Results.Ok(result);
}).RequireAuthorization("Admin");

app.MapFallbackToFile("index.html");
app.Run();

namespace RiffRoster
{
    public sealed record LoginRequest(string? LoginName, string? DisplayName, string? Password, string? AccessCode);
    public sealed record ToggleSelectionRequest(string PerformanceId);
    public sealed record SetLastSelectedEventRequest(string? EventId);
    public sealed record CreateGroupRequest(string? Name);
    public sealed record JoinGroupRequest(string? JoinCode);
    public sealed record CreateEventRequest(string Name, string Location, DateOnly StartDate, DateOnly EndDate);
    public sealed record CreatePerformanceRequest(string Artist, string Stage, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
    public sealed record BulkCreatePerformancesRequest(List<CreatePerformanceRequest>? Performances);
    public sealed record BulkPerformanceImportResult(List<Performance> Created, int SkippedDuplicates);

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
        public List<PushDevice> PushDevices { get; set; } = [];
        public List<ReminderDelivery> ReminderDeliveries { get; set; } = [];
        public List<FestivalGroup> Groups { get; set; } = [];
        public List<GroupMembership> GroupMemberships { get; set; } = [];
    }

    public sealed record FestivalEvent(string Id, string Name, string Location, DateOnly StartDate, DateOnly EndDate, List<FestivalDay> Days);
    public sealed record FestivalDay(string Id, DateOnly Date, string Label, List<Performance> Performances);
    public sealed record Performance(string Id, string Artist, string Stage, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
    public sealed class AppUser
    {
        public string Username { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public DateTimeOffset JoinedAt { get; set; }
        public string LoginName { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string? LastSelectedEventId { get; set; }

        [JsonIgnore]
        public bool HasCredentials =>
            !string.IsNullOrWhiteSpace(LoginName) &&
            !string.IsNullOrWhiteSpace(PasswordSalt) &&
            !string.IsNullOrWhiteSpace(PasswordHash);
    }
    public sealed record UserSelection(string Username, string PerformanceId);
    public sealed record FestivalGroup(
        string Id,
        string Name,
        string CreatedBy,
        string JoinCodeHash,
        DateTimeOffset CreatedAt);
    public sealed record GroupMembership(string GroupId, string Username, DateTimeOffset JoinedAt);
    public sealed record AccountSignInResult(bool Succeeded, AppUser? User, bool Created, bool Claimed, string Message, int StatusCode);

    public static class AccountCredentials
    {
        public static string? Validate(LoginRequest request)
        {
            var loginName = request.LoginName?.Trim();
            var displayName = request.DisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(loginName) || loginName.Length is < 3 or > 32 ||
                loginName.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
                return "Use a private login name of 3–32 letters, numbers, dots, dashes, or underscores.";
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length is < 2 or > 32 ||
                displayName.Any(char.IsControl))
                return "Use a display name between 2 and 32 characters.";
            if (string.IsNullOrEmpty(request.Password) || request.Password.Length is < 10 or > 128)
                return "Use a password between 10 and 128 characters.";
            return null;
        }
    }

    public static class PasswordCredentials
    {
        private const int Iterations = 210_000;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        public static (string Salt, string Hash) Create(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashBytes);
            return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
        }

        public static bool Verify(string password, string saltValue, string hashValue)
        {
            try
            {
                var salt = Convert.FromBase64String(saltValue);
                var expected = Convert.FromBase64String(hashValue);
                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    salt,
                    Iterations,
                    HashAlgorithmName.SHA256,
                    expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    public sealed partial class DataStore
    {
        private const int CurrentDataVersion = 7;
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

        public AccountSignInResult SignInOrRegister(string loginName, string displayName, string password)
        {
            lock (_gate)
            {
                loginName = loginName.Trim();
                displayName = displayName.Trim();
                var loginUser = _state.Users.FirstOrDefault(user =>
                    user.HasCredentials && user.LoginName.Equals(loginName, StringComparison.OrdinalIgnoreCase));
                if (loginUser is not null)
                {
                    if (!loginUser.Username.Equals(displayName, StringComparison.OrdinalIgnoreCase) ||
                        !PasswordCredentials.Verify(password, loginUser.PasswordSalt, loginUser.PasswordHash))
                        return FailedSignIn("Those login details are not valid.", StatusCodes.Status401Unauthorized);

                    return new AccountSignInResult(true, loginUser, false, false, string.Empty, StatusCodes.Status200OK);
                }

                var displayUser = _state.Users.FirstOrDefault(user =>
                    user.Username.Equals(displayName, StringComparison.OrdinalIgnoreCase));
                if (displayUser is not null)
                {
                    if (displayUser.HasCredentials)
                        return FailedSignIn("That private login name or display name is already in use.", StatusCodes.Status409Conflict);

                    var claimedPassword = PasswordCredentials.Create(password);
                    displayUser.LoginName = loginName;
                    displayUser.PasswordSalt = claimedPassword.Salt;
                    displayUser.PasswordHash = claimedPassword.Hash;
                    Save();
                    return new AccountSignInResult(true, displayUser, false, true, string.Empty, StatusCodes.Status200OK);
                }

                var passwordCredentials = PasswordCredentials.Create(password);
                var user = new AppUser
                {
                    Username = displayName,
                    Color = UserColor(displayName),
                    JoinedAt = DateTimeOffset.UtcNow,
                    LoginName = loginName,
                    PasswordSalt = passwordCredentials.Salt,
                    PasswordHash = passwordCredentials.Hash,
                    LastSelectedEventId = _state.Events.FirstOrDefault()?.Id
                };
                _state.Users.Add(user);
                Save();
                return new AccountSignInResult(true, user, true, false, string.Empty, StatusCodes.Status200OK);
            }
        }

        private static AccountSignInResult FailedSignIn(string message, int statusCode) =>
            new(false, null, false, false, message, statusCode);

        private AppUser UserByName(string username) =>
            _state.Users.FirstOrDefault(user => user.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The signed-in account no longer exists.");

        public static object PublicUser(AppUser user) => new
        {
            username = user.Username,
            color = user.Color,
            joinedAt = user.JoinedAt
        };

        public object GetSnapshot(string username, bool isAdmin)
        {
            lock (_gate)
            {
                var currentUser = UserByName(username);
                var canonicalName = currentUser.Username;
                var groupIds = _state.GroupMemberships
                    .Where(membership => membership.Username.Equals(canonicalName, StringComparison.OrdinalIgnoreCase))
                    .Select(membership => membership.GroupId)
                    .ToHashSet(StringComparer.Ordinal);
                var visibleUserNames = _state.GroupMemberships
                    .Where(membership => groupIds.Contains(membership.GroupId))
                    .Select(membership => membership.Username)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                visibleUserNames.Add(canonicalName);
                var groups = _state.Groups
                    .Where(group => groupIds.Contains(group.Id))
                    .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => PublicGroup(group, canonicalName))
                    .ToList();
                return new
                {
                    currentUser = canonicalName,
                    isAdmin,
                    lastSelectedEventId = currentUser.LastSelectedEventId,
                    events = _state.Events,
                    users = _state.Users
                        .Where(user => visibleUserNames.Contains(user.Username))
                        .OrderBy(user => user.JoinedAt)
                        .Select(PublicUser),
                    selections = _state.Selections.Where(selection => visibleUserNames.Contains(selection.Username)),
                    groups
                };
            }
        }

        public object GetHealthSnapshot(bool pushConfigured)
        {
            lock (_gate)
                return new
                {
                    status = "ok",
                    events = _state.Events.Count,
                    users = _state.Users.Count,
                    selections = _state.Selections.Count,
                    pushConfigured,
                    pushDevices = _state.PushDevices.Count,
                    checkedAt = DateTimeOffset.UtcNow
                };
        }

        public object? ToggleSelection(string username, string performanceId)
        {
            lock (_gate)
            {
                var performanceExists = _state.Events.SelectMany(e => e.Days).SelectMany(d => d.Performances).Any(p => p.Id == performanceId);
                if (!performanceExists) return null;

                var user = UserByName(username);
                var existing = _state.Selections.FirstOrDefault(selection =>
                    selection.PerformanceId == performanceId && selection.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase));
                var selected = existing is null;
                if (existing is null)
                    _state.Selections.Add(new UserSelection(user.Username, performanceId));
                else
                {
                    _state.Selections.Remove(existing);
                    RemoveReminderDeliveriesForSelection(user.Username, performanceId);
                }

                Save();
                return new { selected, performanceId, username = user.Username };
            }
        }

        public bool SetLastSelectedEvent(string username, string eventId)
        {
            lock (_gate)
            {
                if (!_state.Events.Any(item => item.Id == eventId)) return false;

                var user = UserByName(username);
                if (user.LastSelectedEventId == eventId) return true;

                user.LastSelectedEventId = eventId;
                Save();
                return true;
            }
        }

        public object CreateGroup(string username, string name)
        {
            lock (_gate)
            {
                var user = UserByName(username);
                if (_state.GroupMemberships.Count(membership =>
                        membership.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase)) >= 20)
                    throw new InvalidOperationException("One account can belong to at most 20 groups.");

                var joinCode = CreateJoinCode();
                var group = new FestivalGroup(
                    $"group-{Guid.NewGuid():N}",
                    name,
                    user.Username,
                    HashJoinCode(joinCode),
                    DateTimeOffset.UtcNow);
                _state.Groups.Add(group);
                _state.GroupMemberships.Add(new GroupMembership(group.Id, user.Username, DateTimeOffset.UtcNow));
                Save();
                return new { group = PublicGroup(group, user.Username), joinCode };
            }
        }

        public object? JoinGroup(string username, string joinCode)
        {
            lock (_gate)
            {
                var user = UserByName(username);
                if (_state.GroupMemberships.Count(membership =>
                        membership.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase)) >= 20)
                    throw new InvalidOperationException("One account can belong to at most 20 groups.");

                var candidateHash = HashJoinCode(joinCode);
                var group = _state.Groups.FirstOrDefault(item =>
                    CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(item.JoinCodeHash),
                        Convert.FromHexString(candidateHash)));
                if (group is null) return null;

                var exists = _state.GroupMemberships.Any(membership =>
                    membership.GroupId == group.Id &&
                    membership.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    _state.GroupMemberships.Add(new GroupMembership(group.Id, user.Username, DateTimeOffset.UtcNow));
                    Save();
                }

                return new { group = PublicGroup(group, user.Username), alreadyMember = exists };
            }
        }

        public object? LeaveGroup(string username, string groupId)
        {
            lock (_gate)
            {
                var user = UserByName(username);
                var membership = _state.GroupMemberships.FirstOrDefault(item =>
                    item.GroupId == groupId && item.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase));
                if (membership is null) return null;

                _state.GroupMemberships.Remove(membership);
                var remaining = _state.GroupMemberships.Where(item => item.GroupId == groupId).ToList();
                var group = _state.Groups.FirstOrDefault(item => item.Id == groupId);
                if (group is not null && remaining.Count == 0)
                    _state.Groups.Remove(group);
                else if (group is not null && group.CreatedBy.Equals(user.Username, StringComparison.OrdinalIgnoreCase))
                {
                    var nextOwner = remaining.OrderBy(item => item.JoinedAt).First().Username;
                    var index = _state.Groups.IndexOf(group);
                    _state.Groups[index] = group with { CreatedBy = nextOwner };
                }

                Save();
                return new { left = true, groupId };
            }
        }

        public object? RegenerateJoinCode(string username, string groupId)
        {
            lock (_gate)
            {
                var user = UserByName(username);
                var group = _state.Groups.FirstOrDefault(item => item.Id == groupId);
                if (group is null || !group.CreatedBy.Equals(user.Username, StringComparison.OrdinalIgnoreCase))
                    return null;

                var joinCode = CreateJoinCode();
                var index = _state.Groups.IndexOf(group);
                _state.Groups[index] = group with { JoinCodeHash = HashJoinCode(joinCode) };
                Save();
                return new { groupId, joinCode };
            }
        }

        private object PublicGroup(FestivalGroup group, string currentUsername)
        {
            var members = _state.GroupMemberships
                .Where(membership => membership.GroupId == group.Id)
                .OrderBy(membership => membership.JoinedAt)
                .Select(membership => _state.Users.FirstOrDefault(user =>
                    user.Username.Equals(membership.Username, StringComparison.OrdinalIgnoreCase)))
                .Where(user => user is not null)
                .Select(user => PublicUser(user!))
                .ToList();
            return new
            {
                group.Id,
                group.Name,
                createdBy = group.CreatedBy,
                isOwner = group.CreatedBy.Equals(currentUsername, StringComparison.OrdinalIgnoreCase),
                members
            };
        }

        private static string CreateJoinCode()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            Span<byte> random = stackalloc byte[12];
            RandomNumberGenerator.Fill(random);
            var code = new string(random.ToArray().Select(value => alphabet[value % alphabet.Length]).ToArray());
            return $"{code[..4]}-{code[4..8]}-{code[8..]}";
        }

        private static string HashJoinCode(string value)
        {
            var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
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

        public object? DeleteEvent(string eventId)
        {
            lock (_gate)
            {
                var eventIndex = _state.Events.FindIndex(item => item.Id == eventId);
                if (eventIndex < 0) return null;

                var festivalEvent = _state.Events[eventIndex];
                var performanceIds = festivalEvent.Days
                    .SelectMany(day => day.Performances)
                    .Select(performance => performance.Id)
                    .ToHashSet(StringComparer.Ordinal);
                var removedSelections = _state.Selections.RemoveAll(selection => performanceIds.Contains(selection.PerformanceId));
                _state.ReminderDeliveries.RemoveAll(delivery => performanceIds.Contains(delivery.PerformanceId));
                _state.Events.RemoveAt(eventIndex);

                var nextEventId = _state.Events.Count == 0
                    ? null
                    : _state.Events[Math.Min(eventIndex, _state.Events.Count - 1)].Id;
                foreach (var user in _state.Users.Where(user => user.LastSelectedEventId == eventId))
                    user.LastSelectedEventId = nextEventId;

                Save();
                return new
                {
                    deletedEventId = festivalEvent.Id,
                    deletedEventName = festivalEvent.Name,
                    removedPerformances = performanceIds.Count,
                    removedSelections,
                    nextEventId
                };
            }
        }

        public BulkPerformanceImportResult? AddPerformances(string eventId, IReadOnlyList<CreatePerformanceRequest> requests)
        {
            lock (_gate)
            {
                var festivalEvent = _state.Events.FirstOrDefault(item => item.Id == eventId);
                if (festivalEvent is null) return null;

                var candidates = new List<(FestivalDay Day, CreatePerformanceRequest Request)>();
                foreach (var request in requests)
                {
                    var localDateTime = request.StartsAt.DateTime;
                    var localDate = DateOnly.FromDateTime(localDateTime);
                    var scheduleDate = localDateTime.TimeOfDay < TimeSpan.FromHours(6) ? localDate.AddDays(-1) : localDate;
                    var day = festivalEvent.Days.FirstOrDefault(item => item.Date == scheduleDate);
                    if (day is null) return null;
                    candidates.Add((day, request));
                }

                var known = festivalEvent.Days
                    .SelectMany(day => day.Performances)
                    .Select(PerformanceKey)
                    .ToHashSet();
                var created = new List<Performance>();
                var skippedDuplicates = 0;

                foreach (var candidate in candidates)
                {
                    var key = PerformanceKey(candidate.Request);
                    if (!known.Add(key))
                    {
                        skippedDuplicates++;
                        continue;
                    }

                    var performance = new Performance(
                        $"perf-{Guid.NewGuid():N}",
                        candidate.Request.Artist.Trim(),
                        candidate.Request.Stage.Trim(),
                        candidate.Request.StartsAt,
                        candidate.Request.EndsAt);
                    candidate.Day.Performances.Add(performance);
                    created.Add(performance);
                }

                if (created.Count > 0)
                {
                    foreach (var day in candidates.Select(candidate => candidate.Day).Distinct())
                        day.Performances.Sort((a, b) => a.StartsAt.CompareTo(b.StartsAt));
                    Save();
                }

                return new BulkPerformanceImportResult(created, skippedDuplicates);
            }
        }

        private static (string Artist, string Stage, DateTimeOffset StartsAt, DateTimeOffset EndsAt) PerformanceKey(Performance performance) =>
            (performance.Artist.Trim().ToUpperInvariant(), performance.Stage.Trim().ToUpperInvariant(), performance.StartsAt, performance.EndsAt);

        private static (string Artist, string Stage, DateTimeOffset StartsAt, DateTimeOffset EndsAt) PerformanceKey(CreatePerformanceRequest performance) =>
            (performance.Artist.Trim().ToUpperInvariant(), performance.Stage.Trim().ToUpperInvariant(), performance.StartsAt, performance.EndsAt);

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

            if (loaded.DataVersion < 4)
            {
                loaded.PushDevices ??= [];
                loaded.ReminderDeliveries ??= [];
                loaded.DataVersion = 4;
                _logger.LogInformation("Updated data to version 4; enabled per-device concert reminders.");
            }

            if (loaded.DataVersion < 5)
            {
                var festivalEvent = loaded.Events.FirstOrDefault(item => item.Id == "brutal-assault-2026");
                var seededEvent = SeedData.Create().Events.First(item => item.Id == "brutal-assault-2026");
                var added = 0;
                if (festivalEvent is not null)
                {
                    foreach (var seededDay in seededEvent.Days)
                    {
                        var targetDay = festivalEvent.Days.FirstOrDefault(day => day.Date == seededDay.Date);
                        if (targetDay is null) continue;
                        foreach (var performance in seededDay.Performances.Where(item => item.Stage == "KAL"))
                        {
                            var exists = targetDay.Performances.Any(item =>
                                item.Stage.Equals(performance.Stage, StringComparison.OrdinalIgnoreCase) &&
                                item.Artist.Equals(performance.Artist, StringComparison.OrdinalIgnoreCase) &&
                                item.StartsAt == performance.StartsAt);
                            if (exists) continue;
                            targetDay.Performances.Add(performance);
                            added++;
                        }
                        targetDay.Performances.Sort((left, right) => left.StartsAt.CompareTo(right.StartsAt));
                    }
                }

                loaded.DataVersion = 5;
                _logger.LogInformation("Updated data to version 5; added {PerformanceCount} missing KAL performances.", added);
            }

            if (loaded.DataVersion < 6)
            {
                loaded.Groups ??= [];
                loaded.GroupMemberships ??= [];
                loaded.DataVersion = 6;
                _logger.LogInformation(
                    "Updated data to version 6; preserved {EventCount} running orders and prepared {UserCount} existing display names for account claiming.",
                    loaded.Events.Count,
                    loaded.Users.Count);
            }

            if (loaded.DataVersion < 7)
            {
                var defaultEventId = loaded.Events.FirstOrDefault()?.Id;
                foreach (var user in loaded.Users.Where(user => string.IsNullOrWhiteSpace(user.LastSelectedEventId)))
                    user.LastSelectedEventId = defaultEventId;

                loaded.DataVersion = 7;
                _logger.LogInformation("Updated data to version 7; enabled per-account festival selection.");
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
