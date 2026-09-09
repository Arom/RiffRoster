using System.Net;
using WebPush;
namespace RiffRoster;

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
            options["headers"] = new Dictionary<string, object>
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
