namespace RiffRoster;

public sealed partial class DataStore
{
    public object UpsertPushSubscription(string username, SavePushSubscriptionRequest request)
    {
        using (EnterState())
        {
            var user = UserByName(username);
            var index = _state.PushDevices.FindIndex(device =>
                device.Endpoint.Equals(request.Endpoint, StringComparison.Ordinal));
            var now = DateTimeOffset.UtcNow;
            PushDevice device;
            if (index >= 0)
            {
                var existing = _state.PushDevices[index];
                if (!existing.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase))
                    _state.ReminderDeliveries.RemoveAll(delivery => delivery.PushDeviceId == existing.Id);
                device = existing with
                {
                    Username = user.Username,
                    P256dh = request.Keys!.P256dh,
                    Auth = request.Keys.Auth,
                    LeadMinutes = request.LeadMinutes,
                    IncludeCrewNames = request.IncludeCrewNames,
                    LastSeenAt = now
                };
                _state.PushDevices[index] = device;
            }
            else
            {
                device = new PushDevice(
                    $"push-{Guid.NewGuid():N}",
                    user.Username,
                    request.Endpoint,
                    request.Keys!.P256dh,
                    request.Keys.Auth,
                    request.LeadMinutes,
                    request.IncludeCrewNames,
                    now,
                    now);
                _state.PushDevices.Add(device);
            }

            Save();
            return new
            {
                enabled = true,
                leadMinutes = device.LeadMinutes,
                includeCrewNames = device.IncludeCrewNames
            };
        }
    }

    public void RemovePushSubscription(string username, string endpoint)
    {
        using (EnterState())
        {
            var device = _state.PushDevices.FirstOrDefault(item =>
                item.Endpoint.Equals(endpoint, StringComparison.Ordinal) &&
                item.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (device is null) return;
            RemovePushDeviceWithoutLock(device.Id);
            Save();
        }
    }

    public PushDevice? GetPushDevice(string username, string endpoint)
    {
        using (EnterState())
            return _state.PushDevices.FirstOrDefault(item =>
                item.Endpoint.Equals(endpoint, StringComparison.Ordinal) &&
                item.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    public void RemovePushDevice(string deviceId)
    {
        using (EnterState())
        {
            if (!RemovePushDeviceWithoutLock(deviceId)) return;
            Save();
        }
    }

    public List<DuePushReminder> GetDuePushReminders(DateTimeOffset now)
    {
        using (EnterState())
        {
            var pruned = _state.ReminderDeliveries.RemoveAll(delivery => delivery.SentAt < now.AddDays(-30));
            if (pruned > 0) Save();

            var schedules = (
                from festivalEvent in _state.Events
                from day in festivalEvent.Days
                from performance in day.Performances
                select new { FestivalEvent = festivalEvent, Day = day, Performance = performance })
                .ToDictionary(item => item.Performance.Id, StringComparer.Ordinal);
            var reminders = new List<DuePushReminder>();

            foreach (var device in _state.PushDevices)
            {
                foreach (var selection in _state.Selections.Where(selection =>
                             selection.Username.Equals(device.Username, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!schedules.TryGetValue(selection.PerformanceId, out var schedule)) continue;
                    var performance = schedule.Performance;
                    var reminderAt = performance.StartsAt.AddMinutes(-device.LeadMinutes);
                    if (reminderAt > now || performance.StartsAt <= now) continue;
                    if (_state.ReminderDeliveries.Any(delivery =>
                            delivery.PushDeviceId == device.Id &&
                            delivery.PerformanceId == performance.Id &&
                            delivery.PerformanceStartsAt == performance.StartsAt))
                        continue;

                    var crewNames = _state.Selections
                        .Where(item => item.PerformanceId == performance.Id &&
                                       !item.Username.Equals(device.Username, StringComparison.OrdinalIgnoreCase) &&
                                       SharesGroup(device.Username, item.Username))
                        .Select(item => item.Username)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    reminders.Add(new DuePushReminder(
                        device,
                        schedule.FestivalEvent.Id,
                        schedule.Day.Id,
                        performance.Id,
                        performance.Artist,
                        performance.Stage,
                        performance.StartsAt,
                        crewNames));
                }
            }

            return reminders;
        }
    }

    public void MarkReminderDelivered(DuePushReminder reminder, DateTimeOffset sentAt)
    {
        using (EnterState())
        {
            if (_state.ReminderDeliveries.Any(delivery =>
                    delivery.PushDeviceId == reminder.Device.Id &&
                    delivery.PerformanceId == reminder.PerformanceId &&
                    delivery.PerformanceStartsAt == reminder.StartsAt))
                return;

            _state.ReminderDeliveries.Add(new ReminderDelivery(
                reminder.Device.Id,
                reminder.PerformanceId,
                reminder.StartsAt,
                sentAt));
            Save();
        }
    }

    private void RemoveReminderDeliveriesForSelection(string username, string performanceId)
    {
        var deviceIds = _state.PushDevices
            .Where(device => device.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
            .Select(device => device.Id)
            .ToHashSet(StringComparer.Ordinal);
        _state.ReminderDeliveries.RemoveAll(delivery =>
            delivery.PerformanceId == performanceId && deviceIds.Contains(delivery.PushDeviceId));
    }

    private bool SharesGroup(string firstUsername, string secondUsername)
    {
        var firstGroups = _state.GroupMemberships
            .Where(membership => membership.Username.Equals(firstUsername, StringComparison.OrdinalIgnoreCase))
            .Select(membership => membership.GroupId)
            .ToHashSet(StringComparer.Ordinal);
        return _state.GroupMemberships.Any(membership =>
            firstGroups.Contains(membership.GroupId) &&
            membership.Username.Equals(secondUsername, StringComparison.OrdinalIgnoreCase));
    }

    private bool RemovePushDeviceWithoutLock(string deviceId)
    {
        var removed = _state.PushDevices.RemoveAll(device => device.Id == deviceId) > 0;
        if (removed)
            _state.ReminderDeliveries.RemoveAll(delivery => delivery.PushDeviceId == deviceId);
        return removed;
    }
}
