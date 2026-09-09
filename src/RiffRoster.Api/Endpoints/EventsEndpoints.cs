using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
namespace RiffRoster;

public static class EventsEndpoints
{
    public static void MapEvents(this RouteGroupBuilder api)
    {
        api.MapPost("/events", (CreateEventRequest request, DataStore store) =>
        {
            var error = ScheduleRules.Validate(request);
            if (error is not null) return Results.BadRequest(new { message = error });

            return Results.Ok(store.CreateEvent(request));
        }).RequireAuthorization("Admin");

        api.MapPost("/events/{eventId}/performances", (string eventId, CreatePerformanceRequest request, DataStore store) =>
        {
            var error = ScheduleRules.Validate(request);
            if (error is not null) return Results.BadRequest(new { message = error });

            var performance = store.AddPerformance(eventId, request);
            return performance is null
                ? Results.NotFound(new { message = "Running order not found, or the performance falls outside its dates." })
                : Results.Ok(performance);
        }).RequireAuthorization("Admin");

        api.MapDelete("/events/{eventId}", (string eventId, DataStore store) =>
        {
            var result = store.DeleteEvent(eventId);
            return result is null
                ? Results.NotFound(new { message = "That festival no longer exists." })
                : Results.Ok(result);
        }).RequireAuthorization("Admin");

        api.MapPut("/events/{eventId}/performances/{performanceId}", (string eventId, string performanceId, CreatePerformanceRequest request, DataStore store) =>
        {
            var error = ScheduleRules.Validate(request);
            if (error is not null) return Results.BadRequest(new { message = error });

            var performance = store.UpdatePerformance(eventId, performanceId, request);
            return performance is null
                ? Results.NotFound(new { message = "Running order or act not found, or the performance falls outside the festival dates." })
                : Results.Ok(performance);
        }).RequireAuthorization("Admin");

        api.MapDelete("/events/{eventId}/performances/{performanceId}", (string eventId, string performanceId, DataStore store) =>
        {
            var result = store.DeletePerformance(eventId, performanceId);
            return result is null
                ? Results.NotFound(new { message = "That act no longer exists." })
                : Results.Ok(result);
        }).RequireAuthorization("Admin");

        api.MapPost("/events/{eventId}/performances/bulk", (string eventId, BulkCreatePerformancesRequest request, DataStore store) =>
        {
            var error = ScheduleRules.ValidateBulk(request.Performances);
            if (error is not null) return Results.BadRequest(new { message = error });

            var result = store.AddPerformances(eventId, request.Performances!);
            return result is null
                ? Results.NotFound(new { message = "Running order not found, or a performance falls outside its dates." })
                : Results.Ok(result);
        }).RequireAuthorization("Admin");
    }
}
