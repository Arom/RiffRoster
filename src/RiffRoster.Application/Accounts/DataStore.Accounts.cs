using System.Security.Cryptography;
using System.Text;
namespace RiffRoster;

public sealed partial class DataStore
{
    public AccountSignInResult SignInOrRegister(string loginName, string displayName, string password)
    {
        using (EnterState())
        {
            loginName = loginName.Trim();
            displayName = displayName.Trim();
            var loginUser = _state.Users.FirstOrDefault(user =>
                user.HasCredentials && user.LoginName.Equals(loginName, StringComparison.OrdinalIgnoreCase));
            if (loginUser is not null)
            {
                if (!loginUser.Username.Equals(displayName, StringComparison.OrdinalIgnoreCase) ||
                    !PasswordCredentials.Verify(password, loginUser.PasswordSalt, loginUser.PasswordHash))
                    return FailedSignIn("Those login details are not valid.", 401);

                return new AccountSignInResult(true, loginUser, false, false, string.Empty, 200);
            }

            var displayUser = _state.Users.FirstOrDefault(user =>
                user.Username.Equals(displayName, StringComparison.OrdinalIgnoreCase));
            if (displayUser is not null)
            {
                if (displayUser.HasCredentials)
                    return FailedSignIn("That private login name or display name is already in use.", 409);

                var claimedPassword = PasswordCredentials.Create(password);
                displayUser.LoginName = loginName;
                displayUser.PasswordSalt = claimedPassword.Salt;
                displayUser.PasswordHash = claimedPassword.Hash;
                Save();
                return new AccountSignInResult(true, displayUser, false, true, string.Empty, 200);
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
            return new AccountSignInResult(true, user, true, false, string.Empty, 200);
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

    private static string UserColor(string username)
    {
        string[] colors = ["#FFB000", "#FF6257", "#70D6A7", "#8FB8FF", "#D59CFF", "#F47FC4", "#7DD3FC"];
        var hash = username.Aggregate(17, (current, character) => unchecked(current * 31 + character));
        return colors[(hash & int.MaxValue) % colors.Length];
    }
}
