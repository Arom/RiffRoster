namespace RiffRoster;

public sealed record BrowserPushKeys(string P256dh, string Auth);
public sealed record SavePushSubscriptionRequest(
    string Endpoint,
    BrowserPushKeys? Keys,
    int LeadMinutes,
    bool IncludeCrewNames);
public sealed record DeletePushSubscriptionRequest(string Endpoint);
public sealed record TestPushRequest(string Endpoint);

public sealed record PushDevice(
    string Id,
    string Username,
    string Endpoint,
    string P256dh,
    string Auth,
    int LeadMinutes,
    bool IncludeCrewNames,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);

public sealed record ReminderDelivery(
    string PushDeviceId,
    string PerformanceId,
    DateTimeOffset PerformanceStartsAt,
    DateTimeOffset SentAt);

public sealed record DuePushReminder(
    PushDevice Device,
    string EventId,
    string DayId,
    string PerformanceId,
    string Artist,
    string Stage,
    DateTimeOffset StartsAt,
    IReadOnlyList<string> CrewNames);
