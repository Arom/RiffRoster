using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RiffRoster;
using WebPush;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace RiffRoster;

public static class ApiHost
{
    public static void Run(string[] args)
    {
        if (args.Any(argument => argument.Equals("--generate-vapid", StringComparison.OrdinalIgnoreCase)))
        {
            var generatedKeys = VapidHelper.GenerateVapidKeys();
            Console.WriteLine(JsonSerializer.Serialize(new { publicKey = generatedKeys.PublicKey, privateKey = generatedKeys.PrivateKey }));
            return;
        }

        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        if (builder.Environment.IsDevelopment()) builder.Logging.AddDebug();
        var security = SecuritySettings.FromConfiguration(builder.Configuration, builder.Environment);
        var pushSettings = PushSettings.FromConfiguration(builder.Configuration);
        var readOnly = string.Equals(builder.Configuration["RIFFROSTER_READ_ONLY"], "true", StringComparison.OrdinalIgnoreCase);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        var dataProtection = builder.Services.AddDataProtection().SetApplicationName("RiffRoster");
        var keysPath = builder.Configuration["RIFFROSTER_KEYS_PATH"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            var resolvedKeysPath = DeploymentPaths.Resolve(keysPath, builder.Environment.ContentRootPath);
            Directory.CreateDirectory(resolvedKeysPath);
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(resolvedKeysPath));
        }

        builder.Services.AddSingleton(security);
        builder.Services.AddSingleton(pushSettings);
        builder.Services.AddRiffRosterPersistence(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton<DataStore>();
        builder.Services.AddSingleton<PushNotificationSender>();
        builder.Services.AddHostedService<BackupService>();
        if (!readOnly) builder.Services.AddHostedService<PushReminderService>();
        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "RiffRoster.Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = async context =>
                {
                    var currentSecurity = context.HttpContext.RequestServices.GetRequiredService<SecuritySettings>();
                    var sessionStamp = context.Principal?.FindFirstValue("riffroster:security_stamp");
                    var authenticationVersion = context.Principal?.FindFirstValue("riffroster:auth_version");
                    if (!string.Equals(sessionStamp, currentSecurity.SessionStamp, StringComparison.Ordinal) ||
                        !string.Equals(authenticationVersion, "2", StringComparison.Ordinal))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("Admin", policy => policy.RequireClaim(ClaimTypes.Role, "admin"));
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("account-actions", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 12,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        });

        var app = builder.Build();
        if (string.Equals(builder.Configuration["RIFFROSTER_STORAGE"], "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            // Fail before binding a port if the schema/import gate is not satisfied.
            using var check = app.Services.GetRequiredService<IStatePersistence>().Open();
        }
        app.Use(async (context, next) =>
        {
            if (readOnly && context.Request.Path.StartsWithSegments("/api") &&
                context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.RetryAfter = "300";
                await context.Response.WriteAsJsonAsync(new { message = "RiffRoster is temporarily read-only for maintenance." });
                return;
            }
            await next(context);
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        var api = app.MapGroup("/api");
        api.MapHealth();
        api.MapAuth();
        api.MapSelections();
        api.MapGroups();
        api.MapPush();
        api.MapEvents();
        app.MapFallbackToFile("index.html");
        app.Run();
    }
}
