using System.Net;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// 🚀 RENDER PORT BINDING: Use 0.0.0.0 to ensure the service is reachable
var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// 1. Updated CORS: Added lowercase version and localhost variants
builder.Services.AddCors(options => {
    options.AddPolicy("AllowReactApp",
        policy => policy.WithOrigins(
                            "http://localhost:5173",
                            "http://localhost:3000",
                            "https://T-Fluffy.github.io", 
                            "https://t-fluffy.github.io" // Browsers often send origin in lowercase
                        ) 
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

// 2. Rate Limiting (Keeping your existing logic)
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter(policyName: "fixed", options =>
    {
        options.PermitLimit = 3;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueLimit = 0;               
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

// 🚀 CRITICAL: Ensure CORS is used BEFORE the rate limiter or controllers
app.UseCors("AllowReactApp");
app.UseRateLimiter();
app.MapControllers();

app.Run();