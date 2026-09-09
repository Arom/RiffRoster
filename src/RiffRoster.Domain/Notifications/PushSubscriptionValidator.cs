using System.Net;
using System.Net.Sockets;
namespace RiffRoster;

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
