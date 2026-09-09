using System.Security.Cryptography;
using System.Text;
namespace RiffRoster;

public sealed partial class DataStore
{
    public FestivalEvent CreateEvent(CreateEventRequest request)
    {
        using (EnterState())
        {
            var eventId = $"event-{Guid.NewGuid():N}";
            var days = new List<FestivalDay>();
            for (var date = request.StartDate; date <= request.EndDate; date = date.AddDays(1))
                days.Add(new FestivalDay($"{eventId}-{date:yyyyMMdd}", date, date.ToString("dddd"), []));

            var festivalEvent = new FestivalEvent(eventId, request.Name.Trim(), request.Location.Trim(), request.StartDate, request.EndDate, days);
            _state.Events.Add(festivalEvent);
            Save();
            return festivalEvent;
        }
    }

    public Performance? AddPerformance(string eventId, CreatePerformanceRequest request)
    {
        using (EnterState())
        {
            var festivalEvent = _state.Events.FirstOrDefault(item => item.Id == eventId);
            if (festivalEvent is null) return null;

            var scheduleDate = ScheduleRules.DayFor(request.StartsAt);
            var day = festivalEvent.Days.FirstOrDefault(item => item.Date == scheduleDate);
            if (day is null) return null;

            var performance = new Performance($"perf-{Guid.NewGuid():N}", request.Artist.Trim(), request.Stage.Trim(), request.StartsAt, request.EndsAt);
            day.Performances.Add(performance);
            day.Performances.Sort((a, b) => a.StartsAt.CompareTo(b.StartsAt));
            Save();
            return performance;
        }
    }

    public object? DeleteEvent(string eventId)
    {
        using (EnterState())
        {
            var eventIndex = _state.Events.FindIndex(item => item.Id == eventId);
            if (eventIndex < 0) return null;

            var festivalEvent = _state.Events[eventIndex];
            var performanceIds = festivalEvent.Days
                .SelectMany(day => day.Performances)
                .Select(performance => performance.Id)
                .ToHashSet(StringComparer.Ordinal);
            var removedSelections = _state.Selections.RemoveAll(selection => performanceIds.Contains(selection.PerformanceId));
            _state.ReminderDeliveries.RemoveAll(delivery => performanceIds.Contains(delivery.PerformanceId));
            _state.Events.RemoveAt(eventIndex);

            var nextEventId = _state.Events.Count == 0
                ? null
                : _state.Events[Math.Min(eventIndex, _state.Events.Count - 1)].Id;
            foreach (var user in _state.Users.Where(user => user.LastSelectedEventId == eventId))
                user.LastSelectedEventId = nextEventId;

            Save();
            return new
            {
                deletedEventId = festivalEvent.Id,
                deletedEventName = festivalEvent.Name,
                removedPerformances = performanceIds.Count,
                removedSelections,
                nextEventId
            };
        }
    }

    public Performance? UpdatePerformance(string eventId, string performanceId, CreatePerformanceRequest request)
    {
        using (EnterState())
        {
            var festivalEvent = _state.Events.FirstOrDefault(item => item.Id == eventId);
            if (festivalEvent is null) return null;

            var sourceDay = festivalEvent.Days.FirstOrDefault(day => day.Performances.Any(item => item.Id == performanceId));
            var existing = sourceDay?.Performances.FirstOrDefault(item => item.Id == performanceId);
            if (sourceDay is null || existing is null) return null;

            var scheduleDate = ScheduleRules.DayFor(request.StartsAt);
            var targetDay = festivalEvent.Days.FirstOrDefault(item => item.Date == scheduleDate);
            if (targetDay is null) return null;

            var updated = existing with
            {
                Artist = request.Artist.Trim(),
                Stage = request.Stage.Trim(),
                StartsAt = request.StartsAt,
                EndsAt = request.EndsAt
            };
            sourceDay.Performances.Remove(existing);
            targetDay.Performances.Add(updated);
            sourceDay.Performances.Sort((left, right) => left.StartsAt.CompareTo(right.StartsAt));
            if (!ReferenceEquals(sourceDay, targetDay))
                targetDay.Performances.Sort((left, right) => left.StartsAt.CompareTo(right.StartsAt));

            _state.ReminderDeliveries.RemoveAll(delivery => delivery.PerformanceId == performanceId);
            Save();
            return updated;
        }
    }

    public object? DeletePerformance(string eventId, string performanceId)
    {
        using (EnterState())
        {
            var festivalEvent = _state.Events.FirstOrDefault(item => item.Id == eventId);
            var day = festivalEvent?.Days.FirstOrDefault(item => item.Performances.Any(performance => performance.Id == performanceId));
            var performance = day?.Performances.FirstOrDefault(item => item.Id == performanceId);
            if (day is null || performance is null) return null;

            day.Performances.Remove(performance);
            var removedSelections = _state.Selections.RemoveAll(selection => selection.PerformanceId == performanceId);
            _state.ReminderDeliveries.RemoveAll(delivery => delivery.PerformanceId == performanceId);
            Save();
            return new
            {
                deletedPerformanceId = performance.Id,
                deletedArtist = performance.Artist,
                removedSelections
            };
        }
    }

    public BulkPerformanceImportResult? AddPerformances(string eventId, IReadOnlyList<CreatePerformanceRequest> requests)
    {
        using (EnterState())
        {
            var festivalEvent = _state.Events.FirstOrDefault(item => item.Id == eventId);
            if (festivalEvent is null) return null;

            var candidates = new List<(FestivalDay Day, CreatePerformanceRequest Request)>();
            foreach (var request in requests)
            {
                var scheduleDate = ScheduleRules.DayFor(request.StartsAt);
                var day = festivalEvent.Days.FirstOrDefault(item => item.Date == scheduleDate);
                if (day is null) return null;
                candidates.Add((day, request));
            }

            var known = festivalEvent.Days
                .SelectMany(day => day.Performances)
                .Select(PerformanceKey)
                .ToHashSet();
            var created = new List<Performance>();
            var skippedDuplicates = 0;

            foreach (var candidate in candidates)
            {
                var key = PerformanceKey(candidate.Request);
                if (!known.Add(key))
                {
                    skippedDuplicates++;
                    continue;
                }

                var performance = new Performance(
                    $"perf-{Guid.NewGuid():N}",
                    candidate.Request.Artist.Trim(),
                    candidate.Request.Stage.Trim(),
                    candidate.Request.StartsAt,
                    candidate.Request.EndsAt);
                candidate.Day.Performances.Add(performance);
                created.Add(performance);
            }

            if (created.Count > 0)
            {
                foreach (var day in candidates.Select(candidate => candidate.Day).Distinct())
                    day.Performances.Sort((a, b) => a.StartsAt.CompareTo(b.StartsAt));
                Save();
            }

            return new BulkPerformanceImportResult(created, skippedDuplicates);
        }
    }

    private static (string Artist, string Stage, DateTimeOffset StartsAt, DateTimeOffset EndsAt) PerformanceKey(Performance performance) =>
        (performance.Artist.Trim().ToUpperInvariant(), performance.Stage.Trim().ToUpperInvariant(), performance.StartsAt, performance.EndsAt);

    private static (string Artist, string Stage, DateTimeOffset StartsAt, DateTimeOffset EndsAt) PerformanceKey(CreatePerformanceRequest performance) =>
        (performance.Artist.Trim().ToUpperInvariant(), performance.Stage.Trim().ToUpperInvariant(), performance.StartsAt, performance.EndsAt);
}
