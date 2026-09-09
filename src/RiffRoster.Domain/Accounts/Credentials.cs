using System.Security.Cryptography;
namespace RiffRoster;

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
