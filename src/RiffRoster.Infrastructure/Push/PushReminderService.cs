using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace RiffRoster;

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
                    logger.LogInformation("Removed an expired push subscription.");
                    continue;
                }

                store.MarkReminderDelivered(reminder, DateTimeOffset.UtcNow);
                logger.LogInformation(
                    "Sent a performance reminder.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Performance reminder failed ({ErrorType}); it will be retried before the performance starts.",
                    exception.GetType().Name);
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
