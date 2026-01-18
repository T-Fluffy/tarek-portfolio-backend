using System.Net;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// 🚀 CLOUD PORT FIX
var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
builder.WebHost.UseUrls($"http://*:{port}");

// 1. CORS Policy
builder.Services.AddCors(options => {
    options.AddPolicy("AllowReactApp",
        policy => policy.WithOrigins(
                            "http://localhost:3000", 
                            "http://localhost:5173",
                            "https://T-Fluffy.github.io" 
                        ) 
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

// 2. Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter(policyName: "fixed", options =>
    {
        options.PermitLimit = 3;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueLimit = 0;               
    });
});

// 3. 🚀 NEW: Configure Resend API Client
// This uses Port 443 (HTTPS), which Render DOES NOT block.
builder.Services.AddHttpClient("ResendClient", client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {builder.Configuration["ResendKey"]}");
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseCors("AllowReactApp");
app.UseRateLimiter();
app.MapControllers();

app.Run();