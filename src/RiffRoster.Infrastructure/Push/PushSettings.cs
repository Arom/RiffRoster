using Microsoft.Extensions.Configuration;
namespace RiffRoster;

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
