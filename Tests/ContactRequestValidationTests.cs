using System.ComponentModel.DataAnnotations;
using Portfolio.Backend.Models;

namespace Portfolio.Backend.Tests;

public class ContactRequestValidationTests
{
    private static List<ValidationResult> Validate(ContactRequest request)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(request);
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void ValidRequest_PassesValidation()
    {
        var request = new ContactRequest
        {
            Name = "Tarek",
            Email = "halloultarek1@gmail.com",
            Subject = "System Inquiry",
            Message = "Hello, this is a test message."
        };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void MissingName_FailsValidation()
    {
        var request = new ContactRequest
        {
            Name = "",
            Email = "test@example.com",
            Subject = "Inquiry",
            Message = "Hello"
        };

        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void InvalidEmail_FailsValidation()
    {
        var request = new ContactRequest
        {
            Name = "Tarek",
            Email = "not-an-email",
            Subject = "Inquiry",
            Message = "Hello"
        };

        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void MissingSubject_FailsValidation()
    {
        var request = new ContactRequest
        {
            Name = "Tarek",
            Email = "test@example.com",
            Subject = "",
            Message = "Hello"
        };

        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void MissingMessage_FailsValidation()
    {
        var request = new ContactRequest
        {
            Name = "Tarek",
            Email = "test@example.com",
            Subject = "Inquiry",
            Message = ""
        };

        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void Honeypot_IsOptional()
    {
        var request = new ContactRequest
        {
            Name = "Tarek",
            Email = "test@example.com",
            Subject = "Inquiry",
            Message = "Hello",
            Honeypot = null
        };

        Assert.Empty(Validate(request));
    }
}