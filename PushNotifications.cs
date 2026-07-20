using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using WebPush;

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

public sealed record PushSettings(string PublicKey, string PrivateKey, string Subject)
{
    public bool Enabled =>
        !string.IsNullOrWhiteSpace(PublicKey) &&
        !string.IsNullOrWhiteSpace(PrivateKey) &&
        !string.IsNullOrWhiteSpace(Subject);

    public static PushSettings FromConfiguration(IConfiguration configuration)
    {
        var publicKey = configuration["RIFFROSTER_VAPID_PUBLIC_KEY"] ?? string.Empty;
        var privateKey = configuration["RIFFROSTER_VAPID_PRIVATE_KEY"] ?? string.Empty;
        var subject = configuration["RIFFROSTER_VAPID_SUBJECT"] ?? string.Empty;
        var configuredValues = new[] { publicKey, privateKey, subject }.Count(value => !string.IsNullOrWhiteSpace(value));

        if (configuredValues is > 0 and < 3)
            throw new InvalidOperationException(
                "Set RIFFROSTER_VAPID_PUBLIC_KEY, RIFFROSTER_VAPID_PRIVATE_KEY, and RIFFROSTER_VAPID_SUBJECT together.");

        if (configuredValues == 3 &&
            (!Uri.TryCreate(subject, UriKind.Absolute, out var subjectUri) ||
             (subjectUri.Scheme != Uri.UriSchemeHttps && subjectUri.Scheme != "mailto")))
            throw new InvalidOperationException("RIFFROSTER_VAPID_SUBJECT must be an https: or mailto: URI.");

        return new PushSettings(publicKey.Trim(), privateKey.Trim(), subject.Trim());
    }
}

public static class PushSubscriptionValidator
{
    private static readonly int[] AllowedLeadMinutes = [10, 20, 30, 60];

    public static string? Validate(SavePushSubscriptionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint) || request.Endpoint.Length > 2048)
            return "A valid push subscription endpoint is required.";
        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            endpoint.IsLoopback ||
            endpoint.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            IsPrivateAddress(endpoint.Host))
            return "The push subscription endpoint must be a public HTTPS address.";
        if (request.Keys is null ||
            string.IsNullOrWhiteSpace(request.Keys.P256dh) || request.Keys.P256dh.Length > 256 ||
            string.IsNullOrWhiteSpace(request.Keys.Auth) || request.Keys.Auth.Length > 128)
            return "The browser did not provide valid push encryption keys.";
        if (!AllowedLeadMinutes.Contains(request.LeadMinutes))
            return "Choose a reminder time of 10, 20, 30, or 60 minutes.";

        return null;
    }

    private static bool IsPrivateAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var address)) return false;
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.Equals(IPAddress.IPv6Loopback);
    }
}

public enum PushSendOutcome
{
    Delivered,
    Expired
}

public sealed class PushNotificationSender(PushSettings settings)
{
    public bool Enabled => settings.Enabled;

    public async Task<PushSendOutcome> SendAsync(
        PushDevice device,
        string payload,
        CancellationToken cancellationToken,
        int timeToLiveSeconds = 3600,
        string? topic = null)
    {
        if (!settings.Enabled)
            throw new InvalidOperationException("Push notifications are not configured.");

        var subscription = new PushSubscription(device.Endpoint, device.P256dh, device.Auth);
        var vapid = new VapidDetails(settings.Subject, settings.PublicKey, settings.PrivateKey);
        var options = new Dictionary<string, object>
        {
            ["vapidDetails"] = vapid,
            ["TTL"] = Math.Clamp(timeToLiveSeconds, 0, 86_400)
        };
        if (!string.IsNullOrWhiteSpace(topic))
        {
            options["headers"] = new Dictionary<string, string>
            {
                ["Urgency"] = "high",
                ["Topic"] = topic
            };
        }

        using var client = new WebPushClient();
        try
        {
            await client.SendNotificationAsync(subscription, payload, options, cancellationToken);
            return PushSendOutcome.Delivered;
        }
        catch (WebPushException exception) when (
            exception.StatusCode == HttpStatusCode.Gone ||
            exception.StatusCode == HttpStatusCode.NotFound)
        {
            return PushSendOutcome.Expired;
        }
    }
}

public sealed class PushReminderService(
    DataStore store,
    PushNotificationSender sender,
    PushSettings settings,
    ILogger<PushReminderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled)
        {
            logger.LogInformation("Concert reminders are disabled because VAPID settings are not configured.");
            return;
        }

        try
        {
            await DeliverDueReminders(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await DeliverDueReminders(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private async Task DeliverDueReminders(CancellationToken cancellationToken)
    {
        foreach (var reminder in store.GetDuePushReminders(DateTimeOffset.UtcNow))
        {
            try
            {
                var crewCopy = reminder.Device.IncludeCrewNames
                    ? CrewCopy(reminder.CrewNames)
                    : string.Empty;
                var minutesUntil = Math.Max(1, (int)Math.Ceiling((reminder.StartsAt - DateTimeOffset.UtcNow).TotalMinutes));
                var body = $"{reminder.Stage} · {reminder.StartsAt:HH:mm}.";
                if (!string.IsNullOrWhiteSpace(crewCopy)) body += $" {crewCopy}";
                var payload = JsonSerializer.Serialize(new
                {
                    title = $"{reminder.Artist} starts in {minutesUntil} minute{(minutesUntil == 1 ? string.Empty : "s")}",
                    body,
                    icon = "/icons/icon-192.png",
                    badge = "/icons/badge-96.png",
                    tag = $"riffroster-{reminder.PerformanceId}",
                    data = new { url = $"/?performance={Uri.EscapeDataString(reminder.PerformanceId)}" }
                });
                var ttl = Math.Max(0, (int)(reminder.StartsAt - DateTimeOffset.UtcNow).TotalSeconds);
                var outcome = await sender.SendAsync(
                    reminder.Device,
                    payload,
                    cancellationToken,
                    ttl,
                    Topic(reminder.PerformanceId));

                if (outcome == PushSendOutcome.Expired)
                {
                    store.RemovePushDevice(reminder.Device.Id);
                    logger.LogInformation("Removed an expired push subscription for {Username}.", reminder.Device.Username);
                    continue;
                }

                store.MarkReminderDelivered(reminder, DateTimeOffset.UtcNow);
                logger.LogInformation(
                    "Sent a reminder for {Artist} to {Username}.",
                    reminder.Artist,
                    reminder.Device.Username);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not send a reminder for {Artist} to {Username}; it will be retried before the performance starts.",
                    reminder.Artist,
                    reminder.Device.Username);
            }
        }
    }

    private static string CrewCopy(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "You're the only one in your crew who picked this.",
        1 => $"{names[0]} is also interested.",
        2 => $"{names[0]} and {names[1]} are also interested.",
        _ => $"{names[0]}, {names[1]} and {names.Count - 2} others are also interested."
    };

    private static string Topic(string performanceId)
    {
        var safe = new string(performanceId.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        return safe.Length <= 32 ? safe : safe[..32];
    }
}

public sealed partial class DataStore
{
    public object UpsertPushSubscription(string username, SavePushSubscriptionRequest request)
    {
        lock (_gate)
        {
            var user = EnsureUser(username);
            var index = _state.PushDevices.FindIndex(device =>
                device.Endpoint.Equals(request.Endpoint, StringComparison.Ordinal));
            var now = DateTimeOffset.UtcNow;
            PushDevice device;
            if (index >= 0)
            {
                var existing = _state.PushDevices[index];
                if (!existing.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase))
                    _state.ReminderDeliveries.RemoveAll(delivery => delivery.PushDeviceId == existing.Id);
                device = existing with
                {
                    Username = user.Username,
                    P256dh = request.Keys!.P256dh,
                    Auth = request.Keys.Auth,
                    LeadMinutes = request.LeadMinutes,
                    IncludeCrewNames = request.IncludeCrewNames,
                    LastSeenAt = now
                };
                _state.PushDevices[index] = device;
            }
            else
            {
                device = new PushDevice(
                    $"push-{Guid.NewGuid():N}",
                    user.Username,
                    request.Endpoint,
                    request.Keys!.P256dh,
                    request.Keys.Auth,
                    request.LeadMinutes,
                    request.IncludeCrewNames,
                    now,
                    now);
                _state.PushDevices.Add(device);
            }

            Save();
            return new
            {
                enabled = true,
                leadMinutes = device.LeadMinutes,
                includeCrewNames = device.IncludeCrewNames
            };
        }
    }

    public void RemovePushSubscription(string username, string endpoint)
    {
        lock (_gate)
        {
            var device = _state.PushDevices.FirstOrDefault(item =>
                item.Endpoint.Equals(endpoint, StringComparison.Ordinal) &&
                item.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (device is null) return;
            RemovePushDeviceWithoutLock(device.Id);
            Save();
        }
    }

    public PushDevice? GetPushDevice(string username, string endpoint)
    {
        lock (_gate)
            return _state.PushDevices.FirstOrDefault(item =>
                item.Endpoint.Equals(endpoint, StringComparison.Ordinal) &&
                item.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    public void RemovePushDevice(string deviceId)
    {
        lock (_gate)
        {
            if (!RemovePushDeviceWithoutLock(deviceId)) return;
            Save();
        }
    }

    public List<DuePushReminder> GetDuePushReminders(DateTimeOffset now)
    {
        lock (_gate)
        {
            var pruned = _state.ReminderDeliveries.RemoveAll(delivery => delivery.SentAt < now.AddDays(-30));
            if (pruned > 0) Save();

            var schedules = (
                from festivalEvent in _state.Events
                from day in festivalEvent.Days
                from performance in day.Performances
                select new { FestivalEvent = festivalEvent, Day = day, Performance = performance })
                .ToDictionary(item => item.Performance.Id, StringComparer.Ordinal);
            var reminders = new List<DuePushReminder>();

            foreach (var device in _state.PushDevices)
            {
                foreach (var selection in _state.Selections.Where(selection =>
                             selection.Username.Equals(device.Username, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!schedules.TryGetValue(selection.PerformanceId, out var schedule)) continue;
                    var performance = schedule.Performance;
                    var reminderAt = performance.StartsAt.AddMinutes(-device.LeadMinutes);
                    if (reminderAt > now || performance.StartsAt <= now) continue;
                    if (_state.ReminderDeliveries.Any(delivery =>
                            delivery.PushDeviceId == device.Id &&
                            delivery.PerformanceId == performance.Id &&
                            delivery.PerformanceStartsAt == performance.StartsAt))
                        continue;

                    var crewNames = _state.Selections
                        .Where(item => item.PerformanceId == performance.Id &&
                                       !item.Username.Equals(device.Username, StringComparison.OrdinalIgnoreCase))
                        .Select(item => item.Username)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    reminders.Add(new DuePushReminder(
                        device,
                        schedule.FestivalEvent.Id,
                        schedule.Day.Id,
                        performance.Id,
                        performance.Artist,
                        performance.Stage,
                        performance.StartsAt,
                        crewNames));
                }
            }

            return reminders;
        }
    }

    public void MarkReminderDelivered(DuePushReminder reminder, DateTimeOffset sentAt)
    {
        lock (_gate)
        {
            if (_state.ReminderDeliveries.Any(delivery =>
                    delivery.PushDeviceId == reminder.Device.Id &&
                    delivery.PerformanceId == reminder.PerformanceId &&
                    delivery.PerformanceStartsAt == reminder.StartsAt))
                return;

            _state.ReminderDeliveries.Add(new ReminderDelivery(
                reminder.Device.Id,
                reminder.PerformanceId,
                reminder.StartsAt,
                sentAt));
            Save();
        }
    }

    private void RemoveReminderDeliveriesForSelection(string username, string performanceId)
    {
        var deviceIds = _state.PushDevices
            .Where(device => device.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
            .Select(device => device.Id)
            .ToHashSet(StringComparer.Ordinal);
        _state.ReminderDeliveries.RemoveAll(delivery =>
            delivery.PerformanceId == performanceId && deviceIds.Contains(delivery.PushDeviceId));
    }

    private bool RemovePushDeviceWithoutLock(string deviceId)
    {
        var removed = _state.PushDevices.RemoveAll(device => device.Id == deviceId) > 0;
        if (removed)
            _state.ReminderDeliveries.RemoveAll(delivery => delivery.PushDeviceId == deviceId);
        return removed;
    }
}
