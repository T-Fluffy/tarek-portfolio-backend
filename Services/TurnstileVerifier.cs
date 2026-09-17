using System.Text.Json;

namespace Portfolio.Backend.Services;

/// <summary>
/// Verifies Cloudflare Turnstile tokens via the siteverify API.
/// </summary>
public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken cancellationToken);
}

public sealed class TurnstileVerifier : ITurnstileVerifier
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TurnstileVerifier> _logger;

    public TurnstileVerifier(
        IHttpClientFactory clientFactory,
        IConfiguration config,
        ILogger<TurnstileVerifier> logger)
    {
        _clientFactory = clientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken cancellationToken)
    {
        var secret = _config["TurnstileSecretKey"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Misconfiguration: the caller decides how to handle a null verifier
            // result, but a missing secret must never read as "human".
            _logger.LogWarning("TurnstileSecretKey is missing; cannot verify captcha token.");
            return false;
        }

        var client = _clientFactory.CreateClient("TurnstileClient");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["secret"] = secret,
            ["response"] = token,
            ["remoteip"] = remoteIp ?? string.Empty,
        });

        try
        {
            using var response = await client.PostAsync("turnstile/v0/siteverify", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Turnstile siteverify returned {StatusCode}.", response.StatusCode);
                return false;
            }

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (doc.RootElement.TryGetProperty("error-codes", out var codes))
            {
                _logger.LogWarning("Turnstile verification failed: {Codes}.", codes.ToString());
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error verifying Turnstile token.");
            return false;
        }
    }
}
