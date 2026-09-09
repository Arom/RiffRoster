namespace RiffRoster;

public sealed record LoginRequest(string? LoginName, string? DisplayName, string? Password, string? AccessCode);
public sealed record ToggleSelectionRequest(string PerformanceId);
public sealed record SetLastSelectedEventRequest(string? EventId);
public sealed record CreateGroupRequest(string? Name);
public sealed record JoinGroupRequest(string? JoinCode);
public sealed record CreateEventRequest(string Name, string Location, DateOnly StartDate, DateOnly EndDate);
public sealed record CreatePerformanceRequest(string Artist, string Stage, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record BulkCreatePerformancesRequest(List<CreatePerformanceRequest>? Performances);
public sealed record BulkPerformanceImportResult(List<Performance> Created, int SkippedDuplicates);
