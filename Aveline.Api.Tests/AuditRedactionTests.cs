using Aveline.Api.Modules.Audit.Services;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #177 — the audit redactor must keep secrets out of BeforeJson/AfterJson
/// (domain-model.md §9, C-5).
/// </summary>
public class AuditRedactionTests
{
    private readonly AuditRedactor _redactor = new();

    [Theory]
    [InlineData("password")]
    [InlineData("secret")]
    [InlineData("token")]
    [InlineData("apiKey")]
    [InlineData("api_key")]
    [InlineData("credentialCiphertext")]
    public void Redact_RemovesSensitiveTopLevelKeys(string key)
    {
        var json = _redactor.Redact(new Dictionary<string, object?> { [key] = "super-secret-value" });

        Assert.NotNull(json);
        Assert.DoesNotContain("super-secret-value", json);
        Assert.Contains(AuditRedactor.RedactedMarker, json);
    }

    [Fact]
    public void Redact_RemovesSensitiveNestedKeys()
    {
        var payload = new
        {
            organization = new { name = "Boutique", integration = new { apiToken = "abc123" } },
        };

        var json = _redactor.Redact(payload);

        Assert.NotNull(json);
        Assert.DoesNotContain("abc123", json);
        Assert.Contains("Boutique", json);
    }

    [Fact]
    public void Redact_PreservesNonSensitiveValues()
    {
        var payload = new { name = "Boutique A", unitsPerBlossom = 1000 };

        var json = _redactor.Redact(payload);

        Assert.NotNull(json);
        Assert.Contains("Boutique A", json);
        Assert.Contains("1000", json);
    }

    [Fact]
    public void Redact_NullPayload_ReturnsNull()
    {
        Assert.Null(_redactor.Redact(null));
    }
}
