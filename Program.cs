using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// 🚀 RENDER PORT BINDING: Use 0.0.0.0 to ensure the service is reachable
var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Trust the forwarder headers set by Render's proxy so the app sees real client IPs/scheme.
// KnownNetworks/KnownProxies are cleared because Render sits directly in front of the app.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = 1;
});

// 1. CORS: browsers normalize origins to lowercase, and WithOrigins lowercases hostnames too
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(
        policy => policy.WithOrigins(
                            "http://localhost:5173",
                            "http://localhost:3000",
                            "https://t-fluffy.github.io"
                        )
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

// 2. Rate Limiting keyed off the real client IP (works behind Render's proxy)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new { status = "ERROR", message = "Too many requests." });
    };
    options.AddPolicy("fixed", httpContext =>
    {
        var clientIp = GetClientIp(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// 3. Resend API Client
var resendKey = builder.Configuration["ResendKey"];
if (string.IsNullOrWhiteSpace(resendKey))
{
    // Fail-visible, not fail-silent: log at startup so a missing Render env var
    // is obvious, and /health below reports degraded instead of healthy.
    Console.Error.WriteLine("CRITICAL: 'ResendKey' is missing. Contact sends will fail until it is set.");
}
builder.Services.AddHttpClient("ResendClient", client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {resendKey}");
});

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Generic error surface: no stack traces / provider details leak to clients.
app.UseExceptionHandler();

// Minimal security headers for an API (Render terminates TLS in front).
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    await next();
});

// 🚀 CRITICAL: Trust forwarded headers, then CORS, then the rate limiter, then controllers
app.UseForwardedHeaders();

app.UseCors();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health", (IConfiguration config) =>
{
    // Degraded (not healthy) when the mail uplink cannot work.
    if (string.IsNullOrWhiteSpace(config["ResendKey"]))
    {
        return Results.Json(new { status = "degraded", reason = "ResendKey missing" }, statusCode: 503);
    }
    return Results.Ok(new { status = "healthy" });
});

app.Run();

// Prefer the client IP forwarded by Render's proxy, falling back to the direct connection.
static string GetClientIp(HttpContext context)
{
    var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwarded))
    {
        var first = forwarded.Split(',')[0].Trim();
        if (IPAddress.TryParse(first, out _))
        {
            return first;
        }
    }

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}