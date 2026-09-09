namespace RiffRoster;

public interface IRow { Guid Id { get; set; } }
public sealed class EventRow : IRow
{
    public Guid Id { get; set; }
    public string LegacyId { get; set; } = "";
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}
public sealed class DayRow : IRow
{
    public Guid Id { get; set; }
    public string LegacyId { get; set; } = "";
    public Guid EventId { get; set; }
    public int Position { get; set; }
    public DateOnly Date { get; set; }
    public string Label { get; set; } = "";
}
public sealed class StageRow : IRow
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string Name { get; set; } = "";
}
public sealed class PerformanceRow : IRow
{
    public Guid Id { get; set; }
    public string LegacyId { get; set; } = "";
    public Guid EventId { get; set; }
    public Guid DayId { get; set; }
    public Guid StageId { get; set; }
    public int Position { get; set; }
    public string Artist { get; set; } = "";
    public DateTimeOffset StartsAtUtc { get; set; }
    public short StartsAtUtcTickRemainder { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
    public short EndsAtUtcTickRemainder { get; set; }
    public int StartOffsetMinutes { get; set; }
    public int EndOffsetMinutes { get; set; }
}
public sealed class AccountRow : IRow
{
    public Guid Id { get; set; }
    public string LegacyId { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public int Position { get; set; }
    public string DisplayName { get; set; } = "";
    public string Color { get; set; } = "";
    public DateTimeOffset JoinedAt { get; set; }
    public short JoinedAtTickRemainder { get; set; }
    public string LoginName { get; set; } = "";
    public string? NormalizedLogin { get; set; }
    public string PasswordSalt { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public Guid? LastSelectedEventId { get; set; }
}
public sealed class SelectionRow : IRow
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid PerformanceId { get; set; }
    public int Position { get; set; }
}
public sealed class GroupRow : IRow
{
    public Guid Id { get; set; }
    public string LegacyId { get; set; } = "";
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public Guid OwnerId { get; set; }
    public string JoinCodeHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public short CreatedAtTickRemainder { get; set; }
}
public sealed class MembershipRow : IRow
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid AccountId { get; set; }
    public int Position { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public short JoinedAtTickRemainder { get; set; }
}
public sealed class SubscriptionRow : IRow
{
    public Guid Id { get; set; }
    public string LegacyId { get; set; } = "";
    public Guid AccountId { get; set; }
    public int Position { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public int LeadMinutes { get; set; }
    public bool IncludeCrewNames { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public short CreatedAtTickRemainder { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public short LastSeenAtTickRemainder { get; set; }
}
public sealed class DeliveryRow : IRow
{
    public Guid Id { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid PerformanceId { get; set; }
    public int Position { get; set; }
    public DateTimeOffset PerformanceStartsAt { get; set; }
    public short PerformanceStartsAtTickRemainder { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public short SentAtTickRemainder { get; set; }
}
public sealed class ImportReceipt : IRow
{
    public Guid Id { get; set; }
    public string SourceSha256 { get; set; } = "";
    public string CanonicalSha256 { get; set; } = "";
    public DateTimeOffset ImportedAt { get; set; }
    public int SourceDataVersion { get; set; }
}
