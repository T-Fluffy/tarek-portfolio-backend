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
builder.Services.AddHttpClient("ResendClient", client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    // This looks for "ResendKey" in your Render Environment Variables
    var key = builder.Configuration["ResendKey"];
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");
});

builder.Services.AddControllers();

var app = builder.Build();

// 🚀 CRITICAL: Trust forwarded headers, then CORS, then the rate limiter, then controllers
app.UseForwardedHeaders();

app.UseCors();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

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