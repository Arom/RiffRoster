using System.Security.Cryptography;
using System.Text;
namespace RiffRoster;

public sealed partial class DataStore
{
    public object CreateGroup(string username, string name)
    {
        using (EnterState())
        {
            var user = UserByName(username);
            if (_state.GroupMemberships.Count(membership =>
                    membership.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase)) >= 20)
                throw new InvalidOperationException("One account can belong to at most 20 groups.");

            var joinCode = CreateJoinCode();
            var group = new FestivalGroup(
                $"group-{Guid.NewGuid():N}",
                name,
                user.Username,
                HashJoinCode(joinCode),
                DateTimeOffset.UtcNow);
            _state.Groups.Add(group);
            _state.GroupMemberships.Add(new GroupMembership(group.Id, user.Username, DateTimeOffset.UtcNow));
            Save();
            return new { group = PublicGroup(group, user.Username), joinCode };
        }
    }

    public object? JoinGroup(string username, string joinCode)
    {
        using (EnterState())
        {
            var user = UserByName(username);
            if (_state.GroupMemberships.Count(membership =>
                    membership.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase)) >= 20)
                throw new InvalidOperationException("One account can belong to at most 20 groups.");

            var candidateHash = HashJoinCode(joinCode);
            var group = _state.Groups.FirstOrDefault(item =>
                CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(item.JoinCodeHash),
                    Convert.FromHexString(candidateHash)));
            if (group is null) return null;

            var exists = _state.GroupMemberships.Any(membership =>
                membership.GroupId == group.Id &&
                membership.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                _state.GroupMemberships.Add(new GroupMembership(group.Id, user.Username, DateTimeOffset.UtcNow));
                Save();
            }

            return new { group = PublicGroup(group, user.Username), alreadyMember = exists };
        }
    }

    public object? LeaveGroup(string username, string groupId)
    {
        using (EnterState())
        {
            var user = UserByName(username);
            var membership = _state.GroupMemberships.FirstOrDefault(item =>
                item.GroupId == groupId && item.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase));
            if (membership is null) return null;

            _state.GroupMemberships.Remove(membership);
            var remaining = _state.GroupMemberships.Where(item => item.GroupId == groupId).ToList();
            var group = _state.Groups.FirstOrDefault(item => item.Id == groupId);
            if (group is not null && remaining.Count == 0)
                _state.Groups.Remove(group);
            else if (group is not null && group.CreatedBy.Equals(user.Username, StringComparison.OrdinalIgnoreCase))
            {
                var nextOwner = remaining.OrderBy(item => item.JoinedAt).First().Username;
                var index = _state.Groups.IndexOf(group);
                _state.Groups[index] = group with { CreatedBy = nextOwner };
            }

            Save();
            return new { left = true, groupId };
        }
    }

    public object? RegenerateJoinCode(string username, string groupId)
    {
        using (EnterState())
        {
            var user = UserByName(username);
            var group = _state.Groups.FirstOrDefault(item => item.Id == groupId);
            if (group is null || !group.CreatedBy.Equals(user.Username, StringComparison.OrdinalIgnoreCase))
                return null;

            var joinCode = CreateJoinCode();
            var index = _state.Groups.IndexOf(group);
            _state.Groups[index] = group with { JoinCodeHash = HashJoinCode(joinCode) };
            Save();
            return new { groupId, joinCode };
        }
    }

    public object? TransferGroupOwner(string username, string groupId, string nextUsername)
    {
        using (EnterState())
        {
            var group = _state.Groups.FirstOrDefault(item => item.Id == groupId);
            if (group is null || !group.CreatedBy.Equals(username, StringComparison.OrdinalIgnoreCase)) return null;
            var members = _state.GroupMemberships.Where(item => item.GroupId == groupId).ToList();
            if (!members.Any(item => item.Username.Equals(username, StringComparison.OrdinalIgnoreCase))) return null;
            var next = members.FirstOrDefault(item => item.Username.Equals(nextUsername, StringComparison.OrdinalIgnoreCase));
            if (next is null) return null;
            if (!group.CreatedBy.Equals(next.Username, StringComparison.OrdinalIgnoreCase))
            {
                var index = _state.Groups.IndexOf(group);
                group = group with { CreatedBy = next.Username };
                _state.Groups[index] = group;
                Save();
            }
            return new { group = PublicGroup(group, username) };
        }
    }

    private object PublicGroup(FestivalGroup group, string currentUsername)
    {
        var members = _state.GroupMemberships
            .Where(membership => membership.GroupId == group.Id)
            .OrderBy(membership => membership.JoinedAt)
            .Select(membership => _state.Users.FirstOrDefault(user =>
                user.Username.Equals(membership.Username, StringComparison.OrdinalIgnoreCase)))
            .Where(user => user is not null)
            .Select(user => PublicUser(user!))
            .ToList();
        return new
        {
            group.Id,
            group.Name,
            createdBy = group.CreatedBy,
            isOwner = group.CreatedBy.Equals(currentUsername, StringComparison.OrdinalIgnoreCase),
            members
        };
    }

    private static string CreateJoinCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> random = stackalloc byte[12];
        RandomNumberGenerator.Fill(random);
        var code = new string(random.ToArray().Select(value => alphabet[value % alphabet.Length]).ToArray());
        return $"{code[..4]}-{code[4..8]}-{code[8..]}";
    }

    private static string HashJoinCode(string value)
    {
        var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

}
