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

public static class PushEndpoints
{
    public static void MapPush(this RouteGroupBuilder api)
    {
        api.MapGet("/push/public-key", (PushSettings settings) =>
            settings.Enabled
                ? Results.Ok(new { publicKey = settings.PublicKey })
                : Results.Json(new { message = "Push notifications are not configured yet." }, statusCode: StatusCodes.Status503ServiceUnavailable))
            .RequireAuthorization();

        api.MapPost("/push/subscriptions", (SavePushSubscriptionRequest request, HttpContext context, DataStore store, PushSettings settings) =>
        {
            if (!settings.Enabled)
                return Results.Json(new { message = "Push notifications are not configured yet." }, statusCode: StatusCodes.Status503ServiceUnavailable);

            var validationMessage = PushSubscriptionValidator.Validate(request);
            return validationMessage is null
                ? Results.Ok(store.UpsertPushSubscription(context.User.Identity!.Name!, request))
                : Results.BadRequest(new { message = validationMessage });
        }).RequireAuthorization();

        api.MapDelete("/push/subscriptions", ([FromBody] DeletePushSubscriptionRequest request, HttpContext context, DataStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.Endpoint))
                return Results.BadRequest(new { message = "A push subscription endpoint is required." });

            store.RemovePushSubscription(context.User.Identity!.Name!, request.Endpoint);
            return Results.NoContent();
        }).RequireAuthorization();

        api.MapPost("/push/test", async (TestPushRequest request, HttpContext context, DataStore store, PushNotificationSender sender, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Endpoint))
                return Results.BadRequest(new { message = "A push subscription endpoint is required." });

            var device = store.GetPushDevice(context.User.Identity!.Name!, request.Endpoint);
            if (device is null)
                return Results.NotFound(new { message = "Enable reminders on this device first." });

            var payload = JsonSerializer.Serialize(new
            {
                title = "RiffRoster reminders are on",
                body = "This device is ready for concert reminders.",
                icon = "/icons/icon-192.png",
                badge = "/icons/badge-96.png",
                tag = "riffroster-test",
                data = new { url = "/" }
            });

            try
            {
                var outcome = await sender.SendAsync(device, payload, cancellationToken);
                if (outcome == PushSendOutcome.Expired)
                {
                    store.RemovePushDevice(device.Id);
                    return Results.Json(new { message = "This browser subscription expired. Enable reminders again." }, statusCode: StatusCodes.Status410Gone);
                }

                return Results.Ok(new { sent = true });
            }
            catch (Exception exception)
            {
                loggerFactory.CreateLogger("RiffRoster.PushTest")
                    .LogWarning("A test push could not be sent ({ErrorType}).", exception.GetType().Name);
                return Results.Json(
                    new { message = "The test notification could not be sent. Try enabling reminders again." },
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }).RequireAuthorization();


    }
}
