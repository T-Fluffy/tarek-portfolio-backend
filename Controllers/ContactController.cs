using Microsoft.AspNetCore.Mvc;
using Portfolio.Backend.Models;
using Portfolio.Backend.Services;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("fixed")]
[RequestSizeLimit(32_768)]
public class ContactController : ControllerBase
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly ITurnstileVerifier _turnstile;
    private readonly IConfiguration _config;
    private readonly ILogger<ContactController> _logger;

    public ContactController(
        IHttpClientFactory clientFactory,
        ITurnstileVerifier turnstile,
        IConfiguration config,
        ILogger<ContactController> logger)
    {
        _clientFactory = clientFactory;
        _turnstile = turnstile;
        _config = config;
        _logger = logger;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendTransmission([FromBody] ContactRequest request, CancellationToken cancellationToken)
    {
        // Honeypot anti-spam: silently pretend success for bots.
        if (!string.IsNullOrWhiteSpace(request.Honeypot))
        {
            return Ok(new { status = "SUCCESS" });
        }

        // Turnstile check is active only once TurnstileSecretKey is configured,
        // so the current frontend keeps working until it sends tokens.
        if (!string.IsNullOrWhiteSpace(_config["TurnstileSecretKey"]))
        {
            if (string.IsNullOrWhiteSpace(request.TurnstileToken))
            {
                return BadRequest(new { status = "ERROR", message = "Captcha verification failed." });
            }

            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (!await _turnstile.VerifyAsync(request.TurnstileToken, remoteIp, cancellationToken))
            {
                return BadRequest(new { status = "ERROR", message = "Captcha verification failed." });
            }
        }

        var name = request.Name.Trim();
        var email = request.Email.Trim();
        var subject = SanitizeSubject(request.Subject?.Trim() ?? string.Empty);
        var message = request.Message.Trim();

        // Defense-in-depth: [Required] allows whitespace-only strings, so reject
        // blanks after trimming even if model validation is bypassed/changed.
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(message))
        {
            return BadRequest(new { status = "ERROR", message = "All fields are required." });
        }

        var client = _clientFactory.CreateClient("ResendClient");

        var emailPayload = new
        {
            from = "onboarding@resend.dev",
            to = "halloultarek1@gmail.com",
            reply_to = email,
            subject = $"[PORTFOLIO] {subject}",
            html = $@"
                <h3>New Portfolio Message</h3>
                <p><strong>From:</strong> {WebUtility.HtmlEncode(name)} ({WebUtility.HtmlEncode(email)})</p>
                <hr/>
                <p>{WebUtility.HtmlEncode(message)}</p>"
        };

        var content = new StringContent(JsonSerializer.Serialize(emailPayload), Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync("emails", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return Ok(new { status = "SUCCESS" });
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Resend rejected email (status {StatusCode}): {Body}", response.StatusCode, responseBody);
            // Do not forward the upstream status code: it leaks provider state
            // (e.g. 401 = bad key, 429 = quota). Return a generic gateway error.
            return StatusCode(StatusCodes.Status502BadGateway, new { status = "ERROR", message = "Uplink failed." });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { status = "ERROR", message = "Request cancelled." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending contact email.");
            return StatusCode(500, new { status = "ERROR", message = "Uplink failed." });
        }
    }

    // Prevent CR/LF (and other control characters) from being injected into the email subject.
    private static string SanitizeSubject(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }
}