using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RiffRoster;

namespace RiffRoster.CharacterizationTests;

public sealed class ApiTests
{
    [Fact]
    public async Task Anonymous_state_is_denied_while_health_and_shell_are_public()
    {
        using var app = new TestApp();
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/state")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        Assert.Contains("RiffRoster", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task Login_cookie_private_identity_toggle_and_logout_contract()
    {
        using var app = new TestApp();
        using var client = app.Client();
        var login = new { loginName = "private-alice", displayName = "Alice", password = "fixture-password-only", accessCode = "friends-local" };
        var response = await client.PostAsJsonAsync("/api/session", login);
        response.EnsureSuccessStatusCode();
        var cookie = string.Join(";", response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        var stateText = await client.GetStringAsync("/api/state");
        Assert.DoesNotContain("private-alice", stateText);
        Assert.DoesNotContain("password", stateText.ToLowerInvariant());
        var state = JsonDocument.Parse(stateText).RootElement;
        var id = state.GetProperty("events")[0].GetProperty("days")[0].GetProperty("performances")[0].GetProperty("id").GetString();
        foreach (var expected in new[] { true, false })
        {
            var result = await client.PostAsJsonAsync("/api/selections/toggle", new { performanceId = id, username = "Impersonated" });
            var payload = await result.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(expected, payload.GetProperty("selected").GetBoolean());
            Assert.Equal("Alice", payload.GetProperty("username").GetString());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/selections/toggle", new { performanceId = "missing" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/session", new { login.loginName, login.displayName, password = "wrong-password", login.accessCode })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/session/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/state")).StatusCode);
        using var returning = await app.Login();
        Assert.Equal(HttpStatusCode.OK, (await returning.GetAsync("/api/state")).StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/events")]
    [InlineData("DELETE", "/api/events/missing")]
    [InlineData("POST", "/api/events/missing/performances")]
    [InlineData("PUT", "/api/events/missing/performances/missing")]
    [InlineData("DELETE", "/api/events/missing/performances/missing")]
    [InlineData("POST", "/api/events/missing/performances/bulk")]
    public async Task Every_admin_mutation_rejects_members(string method, string path)
    {
        using var app = new TestApp();
        using var client = await app.Login();
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Admin_bulk_validation_is_atomic_and_edit_preserves_picks()
    {
        using var app = new TestApp();
        using var admin = await app.Login("Owner", true);
        using var member = await app.Login();
        var created = await admin.PostAsJsonAsync("/api/events", new CreateEventRequest("Test festival", "Fixture venue", new(2026, 8, 4), new(2026, 8, 5)));
        var festival = (await created.Content.ReadFromJsonAsync<FestivalEvent>())!;
        var path = $"/api/events/{festival.Id}/performances";
        var act = new CreatePerformanceRequest("Night act", "Main", DateTimeOffset.Parse("2026-08-05T01:00:00+02:00"), DateTimeOffset.Parse("2026-08-05T02:00:00+02:00"));
        var invalid = act with { StartsAt = act.StartsAt.AddDays(10), EndsAt = act.EndsAt.AddDays(10) };
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync(path + "/bulk", new { performances = new[] { act, invalid } })).StatusCode);
        Assert.Empty(app.ReadState().Events.Single(e => e.Id == festival.Id).Days.SelectMany(d => d.Performances));
        var imported = await admin.PostAsJsonAsync(path + "/bulk", new { performances = new[] { act, act with { Artist = " NIGHT ACT " } } });
        var batch = (await imported.Content.ReadFromJsonAsync<BulkPerformanceImportResult>())!;
        var performance = Assert.Single(batch.Created);
        Assert.Equal(1, batch.SkippedDuplicates);
        Assert.Single(app.ReadState().Events.Single(e => e.Id == festival.Id).Days[0].Performances);
        (await member.PostAsJsonAsync("/api/selections/toggle", new { performanceId = performance.Id })).EnsureSuccessStatusCode();
        var edit = await admin.PutAsJsonAsync(path + "/" + performance.Id, act with { Artist = "Renamed", StartsAt = act.StartsAt.AddHours(18), EndsAt = act.EndsAt.AddHours(18) });
        Assert.Equal(performance.Id, (await edit.Content.ReadFromJsonAsync<Performance>())!.Id);
        Assert.Single(app.ReadState().Selections, s => s.PerformanceId == performance.Id);
        (await member.PutAsJsonAsync("/api/account/last-event", new { eventId = festival.Id })).EnsureSuccessStatusCode();
        Assert.Equal(festival.Id, app.ReadState().Users.Single(u => u.Username == "Alice").LastSelectedEventId);
        var deletion = await admin.DeleteAsync(path + "/" + performance.Id);
        Assert.Equal(1, (await deletion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("removedSelections").GetInt32());
        Assert.Empty(app.ReadState().Selections);
        (await admin.DeleteAsync($"/api/events/{festival.Id}")).EnsureSuccessStatusCode();
        Assert.DoesNotContain(app.ReadState().Events, e => e.Id == festival.Id);
    }

    [Fact]
    public async Task Group_visibility_codes_owner_transfer_and_leave_contract()
    {
        using var app = new TestApp();
        using var alice = await app.Login();
        using var bob = await app.Login("Bob");
        using var eve = await app.Login("Eve");
        var creation = await alice.PostAsJsonAsync("/api/groups", new { name = "Fixture crew" });
        var group = await creation.Content.ReadFromJsonAsync<JsonElement>();
        var id = group.GetProperty("group").GetProperty("id").GetString();
        var code = group.GetProperty("joinCode").GetString();
        Assert.DoesNotContain(code!, File.ReadAllText(app.StorePath));
        (await bob.PostAsJsonAsync("/api/groups/join", new { joinCode = code })).EnsureSuccessStatusCode();
        Assert.Contains("Bob", await alice.GetStringAsync("/api/state"));
        Assert.DoesNotContain("Bob", await eve.GetStringAsync("/api/state"));
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsync($"/api/groups/{id}/join-code", null)).StatusCode);
        (await alice.PostAsync($"/api/groups/{id}/join-code", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await eve.PostAsJsonAsync("/api/groups/join", new { joinCode = code })).StatusCode);
        (await alice.PostAsync($"/api/groups/{id}/leave", null)).EnsureSuccessStatusCode();
        Assert.Equal("Bob", Assert.Single(app.ReadState().Groups).CreatedBy);
        Assert.DoesNotContain("Bob", await alice.GetStringAsync("/api/state"));
        (await bob.PostAsync($"/api/groups/{id}/leave", null)).EnsureSuccessStatusCode();
        Assert.Empty(app.ReadState().Groups);
        Assert.Empty(app.ReadState().GroupMemberships);
    }

    [Fact]
    public async Task Unconfigured_push_is_explicit_and_test_endpoint_is_owned()
    {
        using var app = new TestApp();
        using var client = await app.Login();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/push/public-key")).StatusCode);
        var request = new SavePushSubscriptionRequest("https://push.example.test/device", new("key", "auth"), 20, false);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/push/subscriptions", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/push/test", new { request.Endpoint })).StatusCode);
    }
}
