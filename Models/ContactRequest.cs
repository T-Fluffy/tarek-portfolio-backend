using System.ComponentModel.DataAnnotations;

namespace Portfolio.Backend.Models;

public class ContactRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    [RegularExpression(@".*\S+.*", ErrorMessage = "Name must not be blank.")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    [RegularExpression(@".*\S+.*", ErrorMessage = "Subject must not be blank.")]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [StringLength(5000, MinimumLength = 1)]
    [RegularExpression(@".*\S+.*", ErrorMessage = "Message must not be blank.")]
    public string Message { get; set; } = string.Empty;

    // Honeypot anti-spam field. Legitimate browsers never fill this (it's hidden via CSS).
    // A non-empty value means an automated bot submitted the form.
    // Bounded to prevent unbounded allocation from malicious payloads.
    [StringLength(500)]
    public string? Honeypot { get; set; }
}