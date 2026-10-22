using System.Security.Cryptography;
using System.Text;
namespace RiffRoster;

public sealed partial class DataStore
{
    public object? ToggleSelection(string username, string performanceId)
    {
        using (EnterState())
        {
            var performanceExists = _state.Events.SelectMany(e => e.Days).SelectMany(d => d.Performances).Any(p => p.Id == performanceId);
            if (!performanceExists) return null;

            var user = UserByName(username);
            var existing = _state.Selections.FirstOrDefault(selection =>
                selection.PerformanceId == performanceId && selection.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase));
            var selected = existing is null;
            if (existing is null)
                _state.Selections.Add(new UserSelection(user.Username, performanceId));
            else
            {
                _state.Selections.Remove(existing);
                RemoveReminderDeliveriesForSelection(user.Username, performanceId);
            }

            Save();
            return new { selected, performanceId, username = user.Username };
        }
    }

    private static string EventVersion(FestivalEvent festival) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(festival)));

    public object? SetSelection(string username, string performanceId, bool selected, string? mutationId = null)
    {
        using (EnterState())
        {
            var festival = _state.Events.FirstOrDefault(e => e.Days.SelectMany(d => d.Performances).Any(p => p.Id == performanceId));
            if (festival is null) return null;
            var user = UserByName(username);
            var existing = _state.Selections.FirstOrDefault(selection =>
                selection.PerformanceId == performanceId && selection.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase));
            if (selected && existing is null) _state.Selections.Add(new UserSelection(user.Username, performanceId));
            if (!selected && existing is not null)
            {
                _state.Selections.Remove(existing);
                RemoveReminderDeliveriesForSelection(user.Username, performanceId);
            }
            if (selected != (existing is not null)) Save();
            return new { selected, performanceId, username = user.Username, mutationId, eventId = festival.Id, eventVersion = EventVersion(festival) };
        }
    }

    public bool SetLastSelectedEvent(string username, string eventId)
    {
        using (EnterState())
        {
            if (!_state.Events.Any(item => item.Id == eventId)) return false;

            var user = UserByName(username);
            if (user.LastSelectedEventId == eventId) return true;

            user.LastSelectedEventId = eventId;
            Save();
            return true;
        }
    }

}
