using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RiffRoster;

public sealed record StateReport(int DataVersion, Dictionary<string, int> Counts, string CanonicalSha256);

public static class StateAudit
{
    private static readonly JsonSerializerOptions Json = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new UtcConverter());
        return options;
    }
    private sealed class UtcConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDateTimeOffset();
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUniversalTime());
    }
    public static void Validate(AppState s)
    {
        if (s.DataVersion is < 5 or > 7 || s.Events is null || s.Users is null || s.Selections is null || s.Groups is null || s.GroupMemberships is null || s.PushDevices is null || s.ReminderDeliveries is null)
            throw new InvalidDataException("Unsupported schema or missing collection.");
        static HashSet<string> Keys(IEnumerable<string> values, bool names = false)
        {
            var set = new HashSet<string>(names ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var value in values)
                if (string.IsNullOrWhiteSpace(value) || !set.Add(value)) throw new InvalidDataException("Missing or duplicate key.");
            return set;
        }
        static void Require(bool valid) { if (!valid) throw new InvalidDataException("Invalid value or orphan reference."); }
        var events = Keys(s.Events.Select(e => e.Id));
        var users = Keys(s.Users.Select(u => u.Username), true);
        Keys(s.Users.Where(u => !string.IsNullOrEmpty(u.LoginName)).Select(u => u.LoginName), true);
        var groups = Keys(s.Groups.Select(g => g.Id));
        var devices = Keys(s.PushDevices.Select(d => d.Id));
        var days = s.Events.SelectMany(e => e.Days).ToList();
        Keys(days.Select(d => d.Id));
        var performances = days.SelectMany(d => d.Performances).ToList();
        var acts = Keys(performances.Select(p => p.Id));
        foreach (var e in s.Events)
        {
            Require(e.EndDate >= e.StartDate && !string.IsNullOrWhiteSpace(e.Name) && !string.IsNullOrWhiteSpace(e.Location));
            Require(e.Days.Select(d => d.Date).Distinct().Count() == e.Days.Count);
            foreach (var d in e.Days) Require(d.Date >= e.StartDate && d.Date <= e.EndDate);
        }
        foreach (var p in performances) Require(p.EndsAt > p.StartsAt && !string.IsNullOrWhiteSpace(p.Artist) && !string.IsNullOrWhiteSpace(p.Stage));
        foreach (var u in s.Users) Require(u.LastSelectedEventId is null || events.Contains(u.LastSelectedEventId));
        var selectionPairs = new HashSet<(string, string)>();
        foreach (var item in s.Selections) Require(users.Contains(item.Username) && acts.Contains(item.PerformanceId) && selectionPairs.Add((item.Username.ToUpperInvariant(), item.PerformanceId)));
        var memberships = new HashSet<(string, string)>();
        foreach (var m in s.GroupMemberships) Require(groups.Contains(m.GroupId) && users.Contains(m.Username) && memberships.Add((m.GroupId, m.Username.ToUpperInvariant())));
        foreach (var g in s.Groups) Require(users.Contains(g.CreatedBy) && memberships.Contains((g.Id, g.CreatedBy.ToUpperInvariant())) && g.JoinCodeHash.Length == 64 && g.JoinCodeHash.All(Uri.IsHexDigit));
        Keys(s.PushDevices.Select(d => d.Endpoint));
        foreach (var d in s.PushDevices) Require(users.Contains(d.Username) && d.LeadMinutes is 10 or 20 or 30 or 60);
        var deliveries = new HashSet<(string, string, DateTimeOffset)>();
        foreach (var d in s.ReminderDeliveries) Require(devices.Contains(d.PushDeviceId) && acts.Contains(d.PerformanceId) && deliveries.Add((d.PushDeviceId, d.PerformanceId, d.PerformanceStartsAt)));
    }
    public static StateReport Report(AppState s)
    {
        Validate(s);
        var canonical = new AppState
        {
            DataVersion = 7,
            Events = s.Events.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => e with
            {
                Days = e.Days.OrderBy(d => d.Id, StringComparer.Ordinal)
                .Select(d => d with { Performances = d.Performances.OrderBy(p => p.Id, StringComparer.Ordinal).ToList() }).ToList()
            }).ToList(),
            Users = s.Users.OrderBy(u => u.Username, StringComparer.Ordinal).ToList(),
            Selections = s.Selections.OrderBy(x => x.Username, StringComparer.Ordinal).ThenBy(x => x.PerformanceId, StringComparer.Ordinal).ToList(),
            Groups = s.Groups.OrderBy(x => x.Id, StringComparer.Ordinal).ToList(),
            GroupMemberships = s.GroupMemberships.OrderBy(x => x.GroupId, StringComparer.Ordinal).ThenBy(x => x.Username, StringComparer.Ordinal).ToList(),
            PushDevices = s.PushDevices.OrderBy(x => x.Id, StringComparer.Ordinal).ToList(),
            ReminderDeliveries = s.ReminderDeliveries.OrderBy(x => x.PushDeviceId, StringComparer.Ordinal).ThenBy(x => x.PerformanceId, StringComparer.Ordinal).ThenBy(x => x.PerformanceStartsAt).ToList()
        };
        var counts = new Dictionary<string, int>
        {
            ["events"] = s.Events.Count,
            ["days"] = s.Events.Sum(e => e.Days.Count),
            ["performances"] = s.Events.Sum(e => e.Days.Sum(d => d.Performances.Count)),
            ["users"] = s.Users.Count,
            ["selections"] = s.Selections.Count,
            ["groups"] = s.Groups.Count,
            ["memberships"] = s.GroupMemberships.Count,
            ["subscriptions"] = s.PushDevices.Count,
            ["deliveries"] = s.ReminderDeliveries.Count
        };
        var offsets = canonical.Events.SelectMany(e => e.Days).SelectMany(d => d.Performances).Select(p => new { p.Id, start = p.StartsAt.Offset.TotalMinutes, end = p.EndsAt.Offset.TotalMinutes });
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { state = canonical, offsets }, Json)));
        return new StateReport(s.DataVersion, counts, hash);
    }
}
