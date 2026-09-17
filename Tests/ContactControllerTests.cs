using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Backend.Models;

namespace Portfolio.Backend.Tests;

public class ContactControllerTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public int Calls;
        public HttpRequestMessage? LastRequest;
        public string? LastRequestBody;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            LastRequest = request;
            LastRequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class StubClientFactory : IHttpClientFactory
    {
        private readonly Dictionary<string, HttpClient> _clients;
        public StubClientFactory(Dictionary<string, HttpClient> clients) => _clients = clients;
        public HttpClient CreateClient(string name) => _clients[name];
    }

    private sealed class StubVerifier : Services.ITurnstileVerifier
    {
        private readonly Func<string, string?, CancellationToken, Task<bool>> _verify;
        public int Calls;
        public StubVerifier(Func<string, string?, CancellationToken, Task<bool>> verify) => _verify = verify;
        public Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return _verify(token, remoteIp, cancellationToken);
        }
    }

    private sealed class FakeEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
    }

    private static ContactController BuildController(
        IConfiguration config,
        FakeHandler resendHandler,
        StubVerifier? verifier = null,
        string? environment = null)
    {
        var resendClient = new HttpClient(resendHandler) { BaseAddress = new Uri("https://api.resend.com/") };
        var factory = new StubClientFactory(new Dictionary<string, HttpClient> { ["ResendClient"] = resendClient });
        var controller = new ContactController(
            factory,
            verifier ?? new StubVerifier((_, _, _) => Task.FromResult(true)),
            config,
            new FakeEnvironment(environment ?? Environments.Development),
            NullLogger<ContactController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static IConfiguration ConfigWith(params (string Key, string? Value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.Key, p => p.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
    }

    private static IConfiguration NoTurnstileConfig() => ConfigWith(("ResendKey", "re_test"));

    private static IConfiguration TurnstileConfig() => ConfigWith(("ResendKey", "re_test"), ("TurnstileSecretKey", "ts_test"));

    private static ContactRequest ValidRequest() => new()
    {
        Name = "Tarek",
        Email = "test@example.com",
        Subject = "Hello",
        Message = "Test message",
    };

    [Fact]
    public async Task HoneypotFilled_ReturnsSuccess_WithoutCallingResendOrTurnstile()
    {
        var resend = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var verifier = new StubVerifier((_, _, _) => Task.FromResult(true));
        var controller = BuildController(NoTurnstileConfig(), resend, verifier);
        var request = ValidRequest();
        request.Honeypot = "bot-field-filled";

        var result = await controller.SendTransmission(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("SUCCESS", JsonSerializer.Serialize(ok.Value));
        Assert.Equal(0, resend.Calls);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task ResendSuccess_ReturnsSuccess()
    {
        var resend = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var controller = BuildController(NoTurnstileConfig(), resend);

        var result = await controller.SendTransmission(ValidRequest(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, resend.Calls);
        Assert.Contains("reply_to", resend.LastRequestBody);
        Assert.Contains("test@example.com", resend.LastRequestBody);
    }

    [Fact]
    public async Task ResendFailure_Returns502_NotUpstreamStatus()
    {
        var resend = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var controller = BuildController(NoTurnstileConfig(), resend);

        var result = await controller.SendTransmission(ValidRequest(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, obj.StatusCode);
    }

    [Fact]
    public async Task WhitespaceName_ReturnsBadRequest_WithoutCallingResend()
    {
        var resend = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var controller = BuildController(NoTurnstileConfig(), resend);
        var request = ValidRequest();
        request.Name = "   ";

        var result = await controller.SendTransmission(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, resend.Calls);
    }

    [Fact]
    public async Task TurnstileConfigured_MissingToken_ReturnsBadRequest()
    {
        var resend = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var verifier = new StubVerifier((_, _, _) => Task.FromResult(true));
        var controller = BuildController(TurnstileConfig(), resend, verifier);

        var result = await controller.SendTransmission(ValidRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, resend.Calls);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task TurnstileConfigured_InvalidToken_ReturnsBadRequest()
    {
        var resend = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var verifier = new StubVerifier((_, _, _) => Task.FromResult(false));
        var controller = BuildController(TurnstileConfig(), resend, verifier);
        var request = ValidRequest();
        request.TurnstileToken = "bad-token";

        var result = await controller.SendTransmission(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(1, verifier.Calls);
        Assert.Equal(0, resend.Calls);
    }

    [Fact]
    public async Task TurnstileConfigured_ValidToken_SendsEmail()
    {
        var resend = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var verifier = new StubVerifier((_, _, _) => Task.FromResult(true));
        var controller = BuildController(TurnstileConfig(), resend, verifier);
        var request = ValidRequest();
        request.TurnstileToken = "good-token";

        var result = await controller.SendTransmission(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, verifier.Calls);
        Assert.Equal(1, resend.Calls);
    }

    [Fact]
    public async Task TurnstileVerifier_MissingSecret_ReturnsFalse_WithoutHttpCall()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var factory = new StubClientFactory(new Dictionary<string, HttpClient>
        {
            ["TurnstileClient"] = new HttpClient(handler) { BaseAddress = new Uri("https://challenges.cloudflare.com/") }
        });
        var verifier = new Services.TurnstileVerifier(factory, NoTurnstileConfig(), NullLogger<Services.TurnstileVerifier>.Instance);

        Assert.False(await verifier.VerifyAsync("token", null, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task TurnstileVerifier_SuccessResponse_ReturnsTrue()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\": true}", System.Text.Encoding.UTF8, "application/json")
        });
        var factory = new StubClientFactory(new Dictionary<string, HttpClient>
        {
            ["TurnstileClient"] = new HttpClient(handler) { BaseAddress = new Uri("https://challenges.cloudflare.com/") }
        });
        var verifier = new Services.TurnstileVerifier(factory, TurnstileConfig(), NullLogger<Services.TurnstileVerifier>.Instance);

        Assert.True(await verifier.VerifyAsync("token", "1.2.3.4", CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        Assert.Contains("siteverify", handler.LastRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task TurnstileVerifier_FailureResponse_ReturnsFalse()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\": false, \"error-codes\": [\"invalid-input-response\"]}", System.Text.Encoding.UTF8, "application/json")
        });
        var factory = new StubClientFactory(new Dictionary<string, HttpClient>
        {
            ["TurnstileClient"] = new HttpClient(handler) { BaseAddress = new Uri("https://challenges.cloudflare.com/") }
        });
        var verifier = new Services.TurnstileVerifier(factory, TurnstileConfig(), NullLogger<Services.TurnstileVerifier>.Instance);

        Assert.False(await verifier.VerifyAsync("token", null, CancellationToken.None));
    }

    [Fact]
    public async Task TurnstileMissingSecret_Production_Returns503CaptchaUnavailable()
    {
        var resend = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var verifier = new StubVerifier((_, _, _) => Task.FromResult(true));
        var controller = BuildController(NoTurnstileConfig(), resend, verifier, Environments.Production);

        var result = await controller.SendTransmission(ValidRequest(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, obj.StatusCode);
        Assert.Contains("captcha-unavailable", JsonSerializer.Serialize(obj.Value));
        Assert.Equal(0, resend.Calls);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task TurnstileMissingSecret_Development_SkipsCheck()
    {
        var resend = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var controller = BuildController(NoTurnstileConfig(), resend, environment: Environments.Development);

        var result = await controller.SendTransmission(ValidRequest(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, resend.Calls);
    }

    [Fact]
    public async Task SubjectControlChars_AreStrippedFromResendPayload()
    {
        var resend = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var controller = BuildController(NoTurnstileConfig(), resend);
        var request = ValidRequest();
        request.Subject = "a\r\nb\tc";

        var result = await controller.SendTransmission(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.DoesNotContain("\r", resend.LastRequestBody);
        Assert.DoesNotContain("\n", resend.LastRequestBody);
        Assert.Contains("[PORTFOLIO] a  b c", resend.LastRequestBody);
    }

    [Fact]
    public async Task MessageHtml_IsEncodedInResendPayload()
    {
        var resend = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var controller = BuildController(NoTurnstileConfig(), resend);
        var request = ValidRequest();
        request.Message = "<script>alert(1)</script>";

        var result = await controller.SendTransmission(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        // Parse the JSON payload: System.Text.Json escapes & as \u0026 on the
        // wire, so assert on the decoded html field, not the raw string.
        using var payload = JsonDocument.Parse(resend.LastRequestBody!);
        var html = payload.RootElement.GetProperty("html").GetString()!;
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task EmailWithControlChars_ReturnsBadRequest()
    {
        var resend = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var controller = BuildController(NoTurnstileConfig(), resend);
        var request = ValidRequest();
        request.Email = "test@example.com\r\nBcc: evil@example.com";

        var result = await controller.SendTransmission(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, resend.Calls);
    }
}
