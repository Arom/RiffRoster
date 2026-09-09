namespace RiffRoster;

public static class ScheduleRules
{
    public static DateOnly DayFor(DateTimeOffset startsAt)
    {
        var date = DateOnly.FromDateTime(startsAt.DateTime);
        return startsAt.TimeOfDay < TimeSpan.FromHours(6) ? date.AddDays(-1) : date;
    }
    public static string? Validate(CreateEventRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Location)) return "Name and location are required.";
        if (request.EndDate < request.StartDate || request.EndDate.DayNumber - request.StartDate.DayNumber > 14) return "Choose an end date within 14 days of the start date.";
        return null;
    }
    public static string? Validate(CreatePerformanceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Artist) || string.IsNullOrWhiteSpace(request.Stage)) return "Artist and stage are required.";
        if (request.EndsAt <= request.StartsAt) return "The end time must be after the start time.";
        return null;
    }
    public static string? ValidateBulk(IReadOnlyList<CreatePerformanceRequest>? requests)
    {
        if (requests is null || requests.Count is < 1 or > 500) return "Choose between 1 and 500 performances to import.";
        foreach (var p in requests)
        {
            if (p is null || string.IsNullOrWhiteSpace(p.Artist) || string.IsNullOrWhiteSpace(p.Stage)) return "Every performance needs an artist and stage.";
            if (p.Artist.Length > 160 || p.Stage.Length > 100) return "An artist or stage name is too long.";
            if (p.EndsAt <= p.StartsAt) return "Every end time must be after its start time.";
            if (p.EndsAt - p.StartsAt > TimeSpan.FromHours(24)) return "A performance cannot run for more than 24 hours.";
        }
        return null;
    }
}
