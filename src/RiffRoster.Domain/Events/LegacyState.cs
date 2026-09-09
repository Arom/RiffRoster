using System.Text.Json.Serialization;
namespace RiffRoster;

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
