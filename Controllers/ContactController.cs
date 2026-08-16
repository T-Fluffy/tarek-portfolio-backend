using Microsoft.AspNetCore.Mvc;
using Portfolio.Backend.Models;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("fixed")]
public class ContactController : ControllerBase
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<ContactController> _logger;

    public ContactController(IHttpClientFactory clientFactory, ILogger<ContactController> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendTransmission([FromBody] ContactRequest request)
    {
        // Honeypot anti-spam: silently pretend success for bots.
        if (!string.IsNullOrWhiteSpace(request.Honeypot))
        {
            return Ok(new { status = "SUCCESS" });
        }

        var name = request.Name.Trim();
        var email = request.Email.Trim();
        var subject = SanitizeSubject(request.Subject?.Trim() ?? string.Empty);
        var message = request.Message.Trim();

        var client = _clientFactory.CreateClient("ResendClient");

        var emailPayload = new
        {
            from = "onboarding@resend.dev",
            to = "halloultarek1@gmail.com",
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
            var response = await client.PostAsync("emails", content);

            if (response.IsSuccessStatusCode)
            {
                return Ok(new { status = "SUCCESS" });
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("Resend rejected email (status {StatusCode}): {Body}", response.StatusCode, responseBody);
            return StatusCode((int)response.StatusCode, new { status = "ERROR", message = "Uplink failed." });
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