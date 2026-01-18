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
        from = "onboarding@resend.dev",
        to = "Halloultarek1@gmail.com", 
        subject = $"[PORTFOLIO] {request.Subject}",
        html = $"<h3>Message from {request.Name}</h3><p>{request.Message}</p>"
    };

    var content = new StringContent(JsonSerializer.Serialize(emailPayload), Encoding.UTF8, "application/json");

    try 
    {
        var response = await client.PostAsync("emails", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return Ok(new { status = "SUCCESS" });
        }

        // 🚨 THIS IS THE KEY: This will print the EXACT reason in your Render Logs
        Console.WriteLine($"!!! RESEND REJECTION: {response.StatusCode} - {responseBody}");

        return StatusCode((int)response.StatusCode, responseBody);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"!!! SYSTEM ERROR: {ex.Message}");
        return StatusCode(500, ex.Message);
    }
    }
}