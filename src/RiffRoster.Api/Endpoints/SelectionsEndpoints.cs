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

public static class SelectionsEndpoints
{
    public static void MapSelections(this RouteGroupBuilder api)
    {
        api.MapPost("/selections/toggle", (ToggleSelectionRequest request, HttpContext context, DataStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.PerformanceId))
                return Results.BadRequest(new { message = "A performance is required." });

            var result = store.ToggleSelection(context.User.Identity!.Name!, request.PerformanceId);
            return result is null
                ? Results.NotFound(new { message = "That performance no longer exists." })
                : Results.Ok(result);
        }).RequireAuthorization();

        api.MapPut("/selections/{performanceId}", (string performanceId, SetSelectionRequest request, HttpContext context, DataStore store) =>
        {
            if (request.Selected is null) return Results.BadRequest(new { message = "An explicit selection state is required." });
            if (request.MutationId is { Length: > 128 }) return Results.BadRequest(new { message = "The mutation identifier is too long." });
            var result = store.SetSelection(context.User.Identity!.Name!, performanceId, request.Selected.Value, request.MutationId);
            return result is null
                ? Results.NotFound(new { message = "That performance no longer exists." })
                : Results.Ok(result);
        }).RequireAuthorization();

        api.MapPut("/account/last-event", (SetLastSelectedEventRequest request, HttpContext context, DataStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.EventId))
                return Results.BadRequest(new { message = "A festival is required." });

            return store.SetLastSelectedEvent(context.User.Identity!.Name!, request.EventId)
                ? Results.Ok(new { eventId = request.EventId })
                : Results.NotFound(new { message = "That festival no longer exists." });
        }).RequireAuthorization();


    }
}
