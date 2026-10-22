using System.Security.Cryptography;
using System.Text;
namespace RiffRoster;

public sealed partial class DataStore
{
    public object GetSnapshot(string username, bool isAdmin)
    {
        using (EnterState())
        {
            var currentUser = UserByName(username);
            var canonicalName = currentUser.Username;
            var groupIds = _state.GroupMemberships
                .Where(membership => membership.Username.Equals(canonicalName, StringComparison.OrdinalIgnoreCase))
                .Select(membership => membership.GroupId)
                .ToHashSet(StringComparer.Ordinal);
            var visibleUserNames = _state.GroupMemberships
                .Where(membership => groupIds.Contains(membership.GroupId))
                .Select(membership => membership.Username)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            visibleUserNames.Add(canonicalName);
            var groups = _state.Groups
                .Where(group => groupIds.Contains(group.Id))
                .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => PublicGroup(group, canonicalName))
                .ToList();
            return new
            {
                currentUser = canonicalName,
                isAdmin,
                lastSelectedEventId = currentUser.LastSelectedEventId,
                events = _state.Events,
                eventVersions = _state.Events.ToDictionary(festival => festival.Id, EventVersion),
                users = _state.Users
                    .Where(user => visibleUserNames.Contains(user.Username))
                    .OrderBy(user => user.JoinedAt)
                    .Select(PublicUser),
                selections = _state.Selections.Where(selection => visibleUserNames.Contains(selection.Username)),
                groups
            };
        }
    }

    public object GetHealthSnapshot(bool pushConfigured)
    {
        using (EnterState())
            return new
            {
                status = "ok",
                events = _state.Events.Count,
                users = _state.Users.Count,
                selections = _state.Selections.Count,
                pushConfigured,
                pushDevices = _state.PushDevices.Count,
                checkedAt = DateTimeOffset.UtcNow
            };
    }

}
