using Microsoft.AspNetCore.Mvc;
using FluentEmail.Core;
using Portfolio.Backend.Models;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("fixed")]
public class ContactController : ControllerBase
{
    private readonly IFluentEmail _email;

    public ContactController(IFluentEmail email)
    {
        _email = email;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendTransmission([FromBody] ContactRequest request)
    {
        var email = _email
            .To("Halloultarek1@gmail.com") // the email
            .Subject($"[PORTFOLIO] {request.Subject}")
            .Body($@"
                New Transmission Received:
                ---------------------------
                Sender: {request.Name}
                Email: {request.Email}
                
                Message:
                {request.Message}
            ");

        var response = await email.SendAsync();

        if (response.Successful)
        {
            return Ok(new { status = "SUCCESS", message = "Transmission Delivered." });
        }

        return BadRequest(new { status = "ERROR", message = "Uplink failed." });
    }
}