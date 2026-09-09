namespace RiffRoster.Domain.Tests;

public sealed class ScheduleRulesTests
{
    [Theory]
    [InlineData("2026-08-05T00:15:00+02:00", "2026-08-04")]
    [InlineData("2026-08-05T05:59:00+02:00", "2026-08-04")]
    [InlineData("2026-08-05T06:00:00+02:00", "2026-08-05")]
    [InlineData("2026-10-25T02:30:00+02:00", "2026-10-24")]
    [InlineData("2026-10-25T02:30:00+01:00", "2026-10-24")]
    public void Festival_day_uses_explicit_offset_and_six_am_boundary(string timestamp, string day)
        => Assert.Equal(DateOnly.Parse(day), ScheduleRules.DayFor(DateTimeOffset.Parse(timestamp)));

    [Fact]
    public void Bulk_rejects_empty_null_row_long_duration_and_over_limit()
    {
        var start = DateTimeOffset.Parse("2026-08-04T20:00:00+02:00");
        var act = new CreatePerformanceRequest("Fixture", "Main", start, start.AddHours(1));
        Assert.Null(ScheduleRules.ValidateBulk([act]));
        Assert.NotNull(ScheduleRules.ValidateBulk([]));
        Assert.NotNull(ScheduleRules.ValidateBulk([null!]));
        Assert.NotNull(ScheduleRules.ValidateBulk([act with { EndsAt = start }]));
        Assert.NotNull(ScheduleRules.ValidateBulk([act with { EndsAt = start.AddHours(25) }]));
        Assert.NotNull(ScheduleRules.ValidateBulk(Enumerable.Repeat(act, 501).ToList()));
    }
}
