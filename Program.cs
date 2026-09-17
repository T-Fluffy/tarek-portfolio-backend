using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
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
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = 1;
});

// 1. CORS: exact origins only. Localhost origins exist for local development
// and are never allowed in Production. Methods/headers are narrowed to what
// the contact form actually needs (CORS constrains browsers, not attackers).
var allowedOrigins = new List<string> { "https://t-fluffy.github.io" };
if (builder.Environment.IsDevelopment())
{
    allowedOrigins.Add("http://localhost:5173");
    allowedOrigins.Add("http://localhost:3000");
}
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(
        policy => policy.WithOrigins(allowedOrigins.ToArray())
                        .WithMethods("GET", "POST")
                        .WithHeaders("Content-Type"));
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
        // Key on the DIRECT peer IP captured before UseForwardedHeaders runs
        // (see the middleware below), NOT on any forwarded-header value and
        // NOT on the post-middleware RemoteIpAddress. Rationale, verified by
        // test: with unknown-proxy trust (cleared KnownNetworks/Proxies) the
        // forwarded-headers middleware honors even a lone forged X-Forwarded-For,
        // so anything derived from headers is client-mintable rate buckets.
        // Trade-off: behind Render's proxy all visitors share the egress IP(s),
        // so the bucket is coarser than per-client. Acceptable here because the
        // Turnstile gate (not the limiter) is the primary bot defense, and a
        // coarse bucket fails safe (throttles bursts, never grants bypass).
        var clientIp = httpContext.Items["DirectRemoteIp"] as string
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
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

// 4. Cloudflare Turnstile verification client. The check only enforces once
// TurnstileSecretKey is set (Render env var), so rollout is non-breaking.
if (string.IsNullOrWhiteSpace(builder.Configuration["TurnstileSecretKey"]))
{
    Console.Error.WriteLine("WARNING: 'TurnstileSecretKey' is missing. Captcha verification is skipped until it is set.");
}
builder.Services.AddHttpClient("TurnstileClient", client =>
{
    client.BaseAddress = new Uri("https://challenges.cloudflare.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<Portfolio.Backend.Services.ITurnstileVerifier, Portfolio.Backend.Services.TurnstileVerifier>();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// Suppress the automatic 400 so the honeypot check in ContactController runs
// FIRST: bots sending honeypot + invalid data must get fake SUCCESS, not a
// 400 that leaks the bot-detection signal. The action validates manually.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

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
// Snapshot the direct peer IP FIRST: the rate limiter keys on it (see above)
// so forged forwarded-headers can never mint fresh buckets.
app.Use((context, next) =>
{
    context.Items["DirectRemoteIp"] = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return next(context);
});

app.UseForwardedHeaders();

app.UseCors();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health", (IConfiguration config) =>
{
    // Degraded (not healthy) when the mail uplink or captcha gate cannot work.
    if (string.IsNullOrWhiteSpace(config["ResendKey"]))
    {
        return Results.Json(new { status = "degraded", reason = "ResendKey missing" }, statusCode: 503);
    }
    if (string.IsNullOrWhiteSpace(config["TurnstileSecretKey"]))
    {
        return Results.Json(new { status = "degraded", reason = "TurnstileSecretKey missing" }, statusCode: 503);
    }
    return Results.Ok(new { status = "healthy" });
});

app.Run();

public partial class Program { }