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

public static class HealthEndpoints
{
    public static void MapHealth(this RouteGroupBuilder api)
    {
        api.MapGet("/health", (DataStore store, PushSettings settings) => Results.Ok(store.GetHealthSnapshot(settings.Enabled)))
            .AllowAnonymous();


    }
}
