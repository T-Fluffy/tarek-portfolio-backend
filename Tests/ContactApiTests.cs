using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Portfolio.Backend.Tests;

/// <summary>
/// Full-pipeline tests (routing, CORS, rate limiting, validation ordering)
/// running the real app in-memory. Each test gets its own factory so the
/// fixed-window rate limiter buckets never leak between tests.
/// </summary>
public class ContactApiTests
{
    private sealed class PortfolioBackendFactory(
        IDictionary<string, string?> config,
        string environment = "Development") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(config!));
        }
    }

    private static PortfolioBackendFactory CreateFactory(
        string environment = "Development",
        bool withTurnstileSecret = false)
    {
        var config = new Dictionary<string, string?>
        {
            ["ResendKey"] = "re_test",
        };
        if (withTurnstileSecret)
        {
            config["TurnstileSecretKey"] = "ts_test";
        }
        return new PortfolioBackendFactory(config, environment);
    }

    private static JsonContent HoneypotPayload() => JsonContent.Create(new
    {
        name = "bot",
        email = "bot@example.com",
        subject = "spam",
        message = "spam",
        honeypot = "filled-by-bot",
    });

    private static JsonContent ValidPayload() => JsonContent.Create(new
    {
        name = "Tarek",
        email = "test@example.com",
        subject = "Hello",
        message = "Test message",
        honeypot = "",
    });

    [Fact]
    public async Task RateLimit_Allows3Requests_Then429WithRetryAfter()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            using var allowed = await client.PostAsync("/api/contact/send", HoneypotPayload(), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await client.PostAsync("/api/contact/send", HoneypotPayload(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("60", string.Join(",", rejected.Headers.GetValues("Retry-After")));
        Assert.Contains("ERROR", await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RateLimit_ForgedForwardedForHeader_DoesNotGrantFreshBucket()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            using var allowed = await client.PostAsync("/api/contact/send", HoneypotPayload(), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        // An attacker rotating the client-controlled header must not escape
        // the bucket keyed on the direct peer IP captured pre-forwarding.
        using var spoofed = new HttpRequestMessage(HttpMethod.Post, "/api/contact/send");
        spoofed.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.99");
        spoofed.Content = HoneypotPayload();
        using var rejected = await client.SendAsync(spoofed, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task HoneypotWithInvalidData_ReturnsFakeSuccess_Not400()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Invalid by every validation rule AND honeypot-filled: must still get
        // fake SUCCESS so bots cannot probe the detection signal.
        using var content = JsonContent.Create(new
        {
            name = "",
            email = "not-an-email",
            subject = "",
            message = "",
            honeypot = "filled-by-bot",
        });
        using var response = await client.PostAsync("/api/contact/send", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("SUCCESS", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cors_EvilOrigin_GetsNoAllowOriginHeader()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/contact/send");
        preflight.Headers.Add("Origin", "https://evil.com");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        using var response = await client.SendAsync(preflight, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_AllowedOrigin_GetsAllowOriginHeader()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/contact/send");
        preflight.Headers.Add("Origin", "https://t-fluffy.github.io");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        using var response = await client.SendAsync(preflight, TestContext.Current.CancellationToken);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Contains("https://t-fluffy.github.io", response.Headers.GetValues("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task TurnstileMissingSecret_Production_SendReturns503()
    {
        using var factory = CreateFactory(environment: "Production");
        var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/contact/send", ValidPayload(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("captcha-unavailable", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Health_MissingTurnstileSecret_ReturnsDegraded()
    {
        using var factory = CreateFactory(environment: "Production");
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("degraded", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_AllKeysPresent_ReturnsHealthy()
    {
        using var factory = CreateFactory(environment: "Production", withTurnstileSecret: true);
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
