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
