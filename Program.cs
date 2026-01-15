using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// 🚀 CLOUD PORT FIX: Ensure the app listens to Render's dynamic port
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://*:{port}");

// Retrieve settings (Use Environment Variables in Render Dashboard for these)
var smtpEmail = builder.Configuration["SmtpSettings:Email"];
var smtpPass = builder.Configuration["SmtpSettings:Password"];

// 1. Updated CORS: Added production URL
builder.Services.AddCors(options => {
    options.AddPolicy("AllowReactApp",
        policy => policy.WithOrigins(
                            "http://localhost:3000", 
                            "http://localhost:5173",
                            "https://T-Fluffy.github.io" // 🚀 Your Live Frontend
                        ) 
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

// 2. Add Rate Limiting Policy
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter(policyName: "fixed", options =>
    {
        options.PermitLimit = 3;             // Max 3 emails
        options.Window = TimeSpan.FromMinutes(1); // Per 1 minute
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 0;              
    });
});

// 3. Configure FluentEmail
builder.Services
    .AddFluentEmail("server@tarek.dev")
    .AddSmtpSender(new SmtpClient("smtp.gmail.com")
    {
        Port = 587,
        Credentials = new NetworkCredential(smtpEmail, smtpPass),
        EnableSsl = true,
    });

builder.Services.AddControllers();

var app = builder.Build();

// CRITICAL: Order matters!
app.UseCors("AllowReactApp");
app.UseRateLimiter();

app.MapControllers();

app.Run();