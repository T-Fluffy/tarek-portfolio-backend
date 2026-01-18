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

        // 🚀 Resend API Payload
        var emailPayload = new
        {
            from = "onboarding@resend.dev", // Required for Resend Free Tier
            to = "Halloultarek1@gmail.com",   // Your destination email
            subject = $"[PORTFOLIO] {request.Subject}",
            html = $@"
                <h3>New Message from Portfolio</h3>
                <p><strong>Sender:</strong> {request.Name}</p>
                <p><strong>Email:</strong> {request.Email}</p>
                <hr/>
                <p><strong>Message:</strong></p>
                <p>{request.Message}</p>"
        };

        var json = JsonSerializer.Serialize(emailPayload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try 
        {
            var response = await client.PostAsync("emails", content);

            if (response.IsSuccessStatusCode)
            {
                return Ok(new { status = "SUCCESS", message = "Transmission Delivered." });
            }

            // If Resend returns an error, log it
            var errorDetails = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, new { status = "ERROR", message = "Provider rejected request.", details = errorDetails });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { status = "ERROR", message = "Internal Uplink Failure.", details = ex.Message });
        }
    }
}