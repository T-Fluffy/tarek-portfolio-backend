using Microsoft.AspNetCore.Mvc;
using Portfolio.Backend.Models;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("fixed")]
public class ContactController : ControllerBase
{
    private readonly IHttpClientFactory _clientFactory;

    public ContactController(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendTransmission([FromBody] ContactRequest request)
    {
        var client = _clientFactory.CreateClient("ResendClient");

        var emailPayload = new
        {
            from = "onboarding@resend.dev", // DO NOT CHANGE THIS (Resend Free Tier Rule)
            to = "Halloultarek1@gmail.com",   // Your verified email
            subject = $"[PORTFOLIO] {request.Subject}",
            html = $@"
                <h3>New Portfolio Message</h3>
                <p><strong>From:</strong> {request.Name} ({request.Email})</p>
                <hr/>
                <p>{request.Message}</p>"
        };

        var content = new StringContent(JsonSerializer.Serialize(emailPayload), Encoding.UTF8, "application/json");

        try 
        {
            var response = await client.PostAsync("emails", content);

            if (response.IsSuccessStatusCode)
            {
                return Ok(new { status = "SUCCESS", message = "Transmission Delivered." });
            }

            // This captures the error from Resend (helps debug the 403)
            var errorContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, new { status = "ERROR", details = errorContent });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { status = "ERROR", message = ex.Message });
        }
    }
}