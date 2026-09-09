using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace RiffRoster;

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
