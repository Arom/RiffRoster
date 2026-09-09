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

public static class AuthEndpoints
{
    public static void MapAuth(this RouteGroupBuilder api)
    {
        api.MapPost("/session", async (LoginRequest request, DataStore store, SecuritySettings settings, HttpContext context) =>
        {
            if (!settings.TryAuthorize(request.AccessCode, out var isAdmin))
                return Results.Json(new { message = "Those login details are not valid." }, statusCode: StatusCodes.Status401Unauthorized);

            var validationMessage = AccountCredentials.Validate(request);
            if (validationMessage is not null)
                return Results.BadRequest(new { message = validationMessage });

            var signIn = store.SignInOrRegister(request.LoginName!, request.DisplayName!, request.Password!);
            if (!signIn.Succeeded)
                return Results.Json(new { message = signIn.Message }, statusCode: signIn.StatusCode);

            var user = signIn.User!;
            var claims = new List<Claim>
            {
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, isAdmin ? "admin" : "member"),
        new("riffroster:auth_version", "2"),
        new("riffroster:security_stamp", settings.SessionStamp)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });

            return Results.Ok(new { user = DataStore.PublicUser(user), isAdmin, created = signIn.Created, claimed = signIn.Claimed });
        }).AllowAnonymous().RequireRateLimiting("account-actions");

        api.MapPost("/session/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization();

        api.MapGet("/state", (HttpContext context, DataStore store) =>
        {
            var username = context.User.Identity!.Name!;
            return Results.Ok(store.GetSnapshot(username, context.User.IsInRole("admin")));
        }).RequireAuthorization();


    }
}
