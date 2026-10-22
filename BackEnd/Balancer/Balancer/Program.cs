using System.Security.Claims;
using System.Threading.RateLimiting;
using Balancer.Data;
using Balancer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Balancer.Hubs;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Citim conexiunea SQL Server din appsettings.json.
// Oprim pornirea cu un mesaj clar dacă setarea lipsește.
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found.");

// Înregistrăm contextul bazei de date.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// Folosim doar utilitarul pentru hashul parolelor.
// Nu activăm sistemul de autentificare ASP.NET Core Identity.
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// Configurăm autentificarea proprie cu cookie.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // Prefixul __Host- necesită HTTPS, calea / și lipsa unui Domain.
        options.Cookie.Name = "__Host-Balancer.Auth";
        options.Cookie.Path = "/";

        // Împiedică citirea cookie-ului prin JavaScript.
        options.Cookie.HttpOnly = true;

        // Cookie-ul este transmis doar prin HTTPS.
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        options.Cookie.SameSite = SameSiteMode.Lax;

        // Biletul de autentificare expiră după 30 de minute.
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);

        // Nu prelungim automat durata prin activitatea utilizatorului.
        options.SlidingExpiration = false;

        // Pentru API returnăm 401 în loc de redirecționare către HTML.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode =
                StatusCodes.Status401Unauthorized;

            return Task.CompletedTask;
        };

        // Returnăm 403 dacă utilizatorul nu are permisiunea necesară.
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode =
                StatusCodes.Status403Forbidden;

            return Task.CompletedTask;
        };

        // La cererile autentificate verificăm dacă utilizatorul
        // încă există și dacă are contul activ.
        options.Events.OnValidatePrincipal = async context =>
        {
            var idValue = context.Principal?.FindFirstValue(
                ClaimTypes.NameIdentifier);

            var sessionValue = context.Principal?.FindFirstValue(
                "balancer_session");

            var database = context.HttpContext.RequestServices
                .GetRequiredService<ApplicationDbContext>();

            var isValid = int.TryParse(idValue, out var userId)
                && Guid.TryParse(sessionValue, out var sessionKey)
                && await database.Users.AnyAsync(
                    user => user.Id == userId
                        && user.IsActive
                        && user.SessionKey == sessionKey,
                    context.HttpContext.RequestAborted);

            if (!isValid)
            {
                context.RejectPrincipal();

                await context.HttpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

// Activează serviciile pentru verificarea accesului.
builder.Services.AddAuthorization();

// Frontendul primește tokenul prin /api/auth/csrf
// și îl trimite în antetul X-CSRF-TOKEN la operațiile POST.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";

    options.Cookie.Name = "__Host-Balancer.Csrf";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// Politica este folosită de [EnableRateLimiting("auth")]
// pe controllerul de autentificare.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode =
        StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey:
                context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",

            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// Păstrăm serviciile MVC necesare controllerelor
// și filtrului AutoValidateAntiforgeryToken.
builder.Services.AddControllersWithViews();

// Înregistrează generarea documentului OpenAPI.
builder.Services.AddOpenApi();
// Clientul pentru comunicarea cu serviciul Python.
// Portul implicit este 8000.
builder.Services.AddHttpClient("BalancerAI", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["AIService:BaseUrl"]
        ?? "http://127.0.0.1:8000/");

    client.Timeout = TimeSpan.FromSeconds(15);
});

// Limită separată pentru recomandările AI.
// Folosim identificatorul contului autentificat drept cheie.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("onboarding", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey:
                context.User.FindFirstValue(
                    ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",

            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
// Protejează codurile invitațiilor.
builder.Services.AddDataProtection();

// Serviciul folosit de controllerul de onboarding.
builder.Services.AddScoped<Balancer.Services.OnboardingFlowService>();

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = false;
    options.MaximumReceiveMessageSize = 8 * 1024;
});

builder.Services.AddSingleton<
    IUserIdProvider,
    MessagingUserIdProvider>();

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("chat-requests", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            }));

    options.AddPolicy("chat-send", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("chat-actions", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("chat-connect", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
var app = builder.Build();

// Expunem documentația API doar în dezvoltare.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Redirecționează cererile HTTP către HTTPS.
app.UseHttpsRedirection();

// Identifică ruta cererii înainte de aplicarea politicilor.
app.UseRouting();

// Permitem conexiunea browserului numai din originea aplicației.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments(
        "/api/messaging-hub"))
    {
        var origin = context.Request.Headers.Origin.ToString();

        var allowed =
            builder.Configuration["Frontend:Origin"]
            ?? "https://localhost:4200";

        var websocket = context.Request.Headers.Upgrade
            .ToString()
            .Equals(
                "websocket",
                StringComparison.OrdinalIgnoreCase);

        if ((origin.Length > 0
                && !string.Equals(
                    origin,
                    allowed,
                    StringComparison.OrdinalIgnoreCase))
            || (websocket && origin.Length == 0))
        {
            context.Response.StatusCode =
                StatusCodes.Status403Forbidden;

            return;
        }
    }

    await next(context);
});
// Ordinea contează: identificăm utilizatorul,
// apoi verificăm dacă are acces.
app.UseAuthentication();
app.UseAuthorization();

// Aplică politicile de limitare asociate rutelor.
app.UseRateLimiter();

// Activează rutele controllerelor, inclusiv /api/auth.
app.MapControllers();

app.MapHub<MessagingHub>(
    "/api/messaging-hub",
    options =>
    {
        options.CloseOnAuthenticationExpiration = true;
        options.ApplicationMaxBufferSize = 16 * 1024;
        options.TransportMaxBufferSize = 16 * 1024;
    })
    .RequireRateLimiting("chat-connect");
// Rută simplă pentru verificarea pornirii backendului.
// Nu verifică și conexiunea cu SQL Server.
app.MapGet("/", () => Results.Ok(new
{
    application = "Balancer API",
    status = "Running"
}));

app.Run();