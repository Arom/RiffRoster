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

public static class GroupsEndpoints
{
    public static void MapGroups(this RouteGroupBuilder api)
    {
        api.MapPut("/groups/{groupId}/owner", (string groupId, TransferGroupOwnerRequest request, HttpContext context, DataStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username))
                return Results.BadRequest(new { message = "Choose a current group member." });
            var result = store.TransferGroupOwner(context.User.Identity!.Name!, groupId, request.Username);
            return result is null ? Results.Forbid() : Results.Ok(result);
        }).RequireAuthorization().RequireRateLimiting("account-actions");

        api.MapPost("/groups", (CreateGroupRequest request, HttpContext context, DataStore store) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length is < 2 or > 48)
                return Results.BadRequest(new { message = "Use a group name between 2 and 48 characters." });

            return Results.Ok(store.CreateGroup(context.User.Identity!.Name!, name));
        }).RequireAuthorization();

        api.MapPost("/groups/join", (JoinGroupRequest request, HttpContext context, DataStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.JoinCode))
                return Results.BadRequest(new { message = "Enter a group join code." });

            var result = store.JoinGroup(context.User.Identity!.Name!, request.JoinCode);
            return result is null
                ? Results.NotFound(new { message = "That group join code is not valid." })
                : Results.Ok(result);
        }).RequireAuthorization().RequireRateLimiting("account-actions");

        api.MapPost("/groups/{groupId}/leave", (string groupId, HttpContext context, DataStore store) =>
        {
            var result = store.LeaveGroup(context.User.Identity!.Name!, groupId);
            return result is null
                ? Results.NotFound(new { message = "You are not a member of that group." })
                : Results.Ok(result);
        }).RequireAuthorization();

        api.MapPost("/groups/{groupId}/join-code", (string groupId, HttpContext context, DataStore store) =>
        {
            var result = store.RegenerateJoinCode(context.User.Identity!.Name!, groupId);
            return result is null
                ? Results.Forbid()
                : Results.Ok(result);
        }).RequireAuthorization().RequireRateLimiting("account-actions");


    }
}
