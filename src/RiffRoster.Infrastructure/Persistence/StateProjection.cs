using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace RiffRoster;

public static class StableImportId
{
    // RFC 9562 UUIDv8: namespaced SHA-256, framed as JSON to avoid separator collisions.
    // Persisted UUIDs are foreign keys. Legacy aliases are compatibility lookup values only.
    public static Guid For(params string[] parts)
    {
        var bytes = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { "RiffRoster/import/v1" }.Concat(parts)));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes.AsSpan(0, 16), bigEndian: true);
    }
}

public static class StateProjection
{
    public static AppState Read(RiffRosterDbContext db)
    {
        var events = db.Set<EventRow>().OrderBy(x => x.Position).ToList();
        var days = db.Set<DayRow>().OrderBy(x => x.Position).ToList();
        var stages = db.Set<StageRow>().ToDictionary(x => x.Id);
        var performances = db.Set<PerformanceRow>().OrderBy(x => x.Position).ToList();
        var accounts = db.Set<AccountRow>().OrderBy(x => x.Position).ToList();
        var groups = db.Set<GroupRow>().OrderBy(x => x.Position).ToList();
        var devices = db.Set<SubscriptionRow>().OrderBy(x => x.Position).ToList();
        var eventIds = events.ToDictionary(x => x.Id, x => x.LegacyId);
        var actIds = performances.ToDictionary(x => x.Id, x => x.LegacyId);
        var names = accounts.ToDictionary(x => x.Id, x => x.DisplayName);
        var groupIds = groups.ToDictionary(x => x.Id, x => x.LegacyId);
        var deviceIds = devices.ToDictionary(x => x.Id, x => x.LegacyId);
        return new AppState
        {
            DataVersion = 7,
            Events = events.Select(e => new FestivalEvent(e.LegacyId, e.Name, e.Location, e.StartDate, e.EndDate,
                days.Where(d => d.EventId == e.Id).Select(d => new FestivalDay(d.LegacyId, d.Date, d.Label,
                    performances.Where(p => p.DayId == d.Id).Select(p => new Performance(p.LegacyId, p.Artist, stages[p.StageId].Name,
                        p.StartsAtUtc.AddTicks(p.StartsAtUtcTickRemainder).ToOffset(TimeSpan.FromMinutes(p.StartOffsetMinutes)), p.EndsAtUtc.AddTicks(p.EndsAtUtcTickRemainder).ToOffset(TimeSpan.FromMinutes(p.EndOffsetMinutes)))).ToList())).ToList())).ToList(),
            Users = accounts.Select(a => new AppUser
            {
                Username = a.DisplayName,
                Color = a.Color,
                JoinedAt = a.JoinedAt.AddTicks(a.JoinedAtTickRemainder),
                LoginName = a.LoginName,
                PasswordSalt = a.PasswordSalt,
                PasswordHash = a.PasswordHash,
                LastSelectedEventId = a.LastSelectedEventId is { } id ? eventIds[id] : null
            }).ToList(),
            Selections = db.Set<SelectionRow>().OrderBy(x => x.Position).AsEnumerable().Select(s => new UserSelection(names[s.AccountId], actIds[s.PerformanceId])).ToList(),
            Groups = groups.Select(g => new FestivalGroup(g.LegacyId, g.Name, names[g.OwnerId], g.JoinCodeHash, g.CreatedAt.AddTicks(g.CreatedAtTickRemainder))).ToList(),
            GroupMemberships = db.Set<MembershipRow>().OrderBy(x => x.Position).AsEnumerable().Select(m => new GroupMembership(groupIds[m.GroupId], names[m.AccountId], m.JoinedAt.AddTicks(m.JoinedAtTickRemainder))).ToList(),
            PushDevices = devices.Select(d => new PushDevice(d.LegacyId, names[d.AccountId], d.Endpoint, d.P256dh, d.Auth, d.LeadMinutes, d.IncludeCrewNames, d.CreatedAt.AddTicks(d.CreatedAtTickRemainder), d.LastSeenAt.AddTicks(d.LastSeenAtTickRemainder))).ToList(),
            ReminderDeliveries = db.Set<DeliveryRow>().OrderBy(x => x.Position).AsEnumerable().Select(d => new ReminderDelivery(deviceIds[d.SubscriptionId], actIds[d.PerformanceId], d.PerformanceStartsAt.AddTicks(d.PerformanceStartsAtTickRemainder), d.SentAt.AddTicks(d.SentAtTickRemainder))).ToList()
        };
    }

    public static void Write(RiffRosterDbContext db, AppState state)
    {
        StateAudit.Validate(state);
        var removals = new List<object>();
        void Apply<T>(IEnumerable<T> rows) where T : class, IRow => Sync(db, rows, removals);
        var eventIds = Aliases<EventRow>(db, "event", state.Events.Select(x => x.Id));
        var dayIds = Aliases<DayRow>(db, "day", state.Events.SelectMany(e => e.Days).Select(x => x.Id));
        var actIds = Aliases<PerformanceRow>(db, "performance", state.Events.SelectMany(e => e.Days).SelectMany(d => d.Performances).Select(x => x.Id));
        var userIds = Aliases<AccountRow>(db, "account", state.Users.Select(x => x.Username));
        // Legacy account matching is case-insensitive; DB foreign keys are UUIDs.
        var accountIds = userIds.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var groupIds = Aliases<GroupRow>(db, "group", state.Groups.Select(x => x.Id));
        var deviceIds = Aliases<SubscriptionRow>(db, "subscription", state.PushDevices.Select(x => x.Id));
        var eventRows = state.Events.Select((e, i) => new EventRow { Id = eventIds[e.Id], LegacyId = e.Id, Position = i, Name = e.Name, Location = e.Location, StartDate = e.StartDate, EndDate = e.EndDate }).ToList();
        var dayRows = new List<DayRow>();
        var stageRows = new List<StageRow>();
        var actRows = new List<PerformanceRow>();
        var existingStages = db.Set<StageRow>().ToList().ToDictionary(s => (s.EventId, s.Name));
        foreach (var e in state.Events)
        {
            var eid = eventIds[e.Id];
            var stages = e.Days.SelectMany(d => d.Performances).Select(p => p.Stage).Distinct(StringComparer.Ordinal)
                .ToDictionary(name => name, name => existingStages.GetValueOrDefault((eid, name))?.Id ?? StableImportId.For("stage", e.Id, name));
            stageRows.AddRange(stages.Select(s => new StageRow { Id = s.Value, EventId = eid, Name = s.Key }));
            foreach (var (day, index) in e.Days.Select((d, i) => (d, i)))
            {
                dayRows.Add(new DayRow { Id = dayIds[day.Id], LegacyId = day.Id, EventId = eid, Position = index, Date = day.Date, Label = day.Label });
                actRows.AddRange(day.Performances.Select((p, i) => new PerformanceRow
                {
                    Id = actIds[p.Id],
                    LegacyId = p.Id,
                    EventId = eid,
                    DayId = dayIds[day.Id],
                    StageId = stages[p.Stage],
                    Position = i,
                    Artist = p.Artist,
                    StartsAtUtc = TimestampPrecision.Database(p.StartsAt),
                    StartsAtUtcTickRemainder = TimestampPrecision.Remainder(p.StartsAt),
                    EndsAtUtc = TimestampPrecision.Database(p.EndsAt),
                    EndsAtUtcTickRemainder = TimestampPrecision.Remainder(p.EndsAt),
                    StartOffsetMinutes = (int)p.StartsAt.Offset.TotalMinutes,
                    EndOffsetMinutes = (int)p.EndsAt.Offset.TotalMinutes
                }));
            }
        }
        Apply(eventRows); Apply(dayRows); Apply(stageRows); Apply(actRows);
        Apply(state.Users.Select((u, i) => new AccountRow
        {
            Id = userIds[u.Username],
            LegacyId = u.Username,
            NormalizedName = u.Username.ToUpperInvariant(),
            DisplayName = u.Username,
            Position = i,
            Color = u.Color,
            JoinedAt = TimestampPrecision.Database(u.JoinedAt),
            JoinedAtTickRemainder = TimestampPrecision.Remainder(u.JoinedAt),
            LoginName = u.LoginName,
            NormalizedLogin = string.IsNullOrEmpty(u.LoginName) ? null : u.LoginName.ToUpperInvariant(),
            PasswordSalt = u.PasswordSalt,
            PasswordHash = u.PasswordHash,
            LastSelectedEventId = u.LastSelectedEventId is { } id ? eventIds[id] : null
        }));
        Apply(state.Selections.Select((s, i) => new SelectionRow
        {
            Id = StableImportId.For("selection", accountIds[s.Username].ToString(), actIds[s.PerformanceId].ToString()),
            AccountId = accountIds[s.Username],
            PerformanceId = actIds[s.PerformanceId],
            Position = i
        }));
        Apply(state.Groups.Select((g, i) => new GroupRow
        {
            Id = groupIds[g.Id],
            LegacyId = g.Id,
            Position = i,
            Name = g.Name,
            OwnerId = accountIds[g.CreatedBy],
            JoinCodeHash = g.JoinCodeHash,
            CreatedAt = TimestampPrecision.Database(g.CreatedAt),
            CreatedAtTickRemainder = TimestampPrecision.Remainder(g.CreatedAt)
        }));
        Apply(state.GroupMemberships.Select((m, i) => new MembershipRow
        {
            Id = StableImportId.For("membership", groupIds[m.GroupId].ToString(), accountIds[m.Username].ToString()),
            GroupId = groupIds[m.GroupId],
            AccountId = accountIds[m.Username],
            JoinedAt = TimestampPrecision.Database(m.JoinedAt),
            JoinedAtTickRemainder = TimestampPrecision.Remainder(m.JoinedAt),
            Position = i
        }));
        Apply(state.PushDevices.Select((d, i) => new SubscriptionRow
        {
            Id = deviceIds[d.Id],
            LegacyId = d.Id,
            AccountId = accountIds[d.Username],
            Position = i,
            Endpoint = d.Endpoint,
            P256dh = d.P256dh,
            Auth = d.Auth,
            LeadMinutes = d.LeadMinutes,
            IncludeCrewNames = d.IncludeCrewNames,
            CreatedAt = TimestampPrecision.Database(d.CreatedAt),
            CreatedAtTickRemainder = TimestampPrecision.Remainder(d.CreatedAt),
            LastSeenAt = TimestampPrecision.Database(d.LastSeenAt),
            LastSeenAtTickRemainder = TimestampPrecision.Remainder(d.LastSeenAt)
        }));
        Apply(state.ReminderDeliveries.Select((d, i) => new DeliveryRow
        {
            Id = StableImportId.For("delivery", deviceIds[d.PushDeviceId].ToString(), actIds[d.PerformanceId].ToString(), d.PerformanceStartsAt.UtcDateTime.ToString("O")),
            SubscriptionId = deviceIds[d.PushDeviceId],
            PerformanceId = actIds[d.PerformanceId],
            Position = i,
            PerformanceStartsAt = TimestampPrecision.Database(d.PerformanceStartsAt),
            PerformanceStartsAtTickRemainder = TimestampPrecision.Remainder(d.PerformanceStartsAt),
            SentAt = TimestampPrecision.Database(d.SentAt),
            SentAtTickRemainder = TimestampPrecision.Remainder(d.SentAt)
        }));
        db.ChangeTracker.DetectChanges();
        foreach (var removed in removals.AsEnumerable().Reverse()) db.Remove(removed);
    }

    private static Dictionary<string, Guid> Aliases<T>(RiffRosterDbContext db, string kind, IEnumerable<string> aliases) where T : class, IRow
    {
        var existing = db.Set<T>().Select(row => new { Alias = EF.Property<string>(row, "LegacyId"), row.Id }).ToDictionary(x => x.Alias, x => x.Id);
        return aliases.ToDictionary(alias => alias, alias => existing.GetValueOrDefault(alias, StableImportId.For(kind, alias)));
    }
    private static void Sync<T>(RiffRosterDbContext db, IEnumerable<T> desired, List<object> removals) where T : class, IRow
    {
        var old = db.Set<T>().ToDictionary(x => x.Id);
        foreach (var row in desired)
        {
            if (old.Remove(row.Id, out var existing)) db.Entry(existing).CurrentValues.SetValues(row);
            else db.Add(row);
        }
        removals.AddRange(old.Values);
    }
}


// PostgreSQL stores microseconds; legacy .NET instants also carry a final 100ns digit.
public static class TimestampPrecision
{
    public static short Remainder(DateTimeOffset value) => (short)(value.UtcTicks % 10);
    public static DateTimeOffset Database(DateTimeOffset value) => new(value.UtcTicks - Remainder(value), TimeSpan.Zero);
}
