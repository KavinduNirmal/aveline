using Microsoft.AspNetCore.Mvc.Testing;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 3.1, the wiring half: a host whose <c>Privacy:LinkSigningKey</c> is present but unusable
/// must refuse to boot, because such a host cannot sign the permanent opt-out link and would
/// message customers without one.
/// </summary>
public class PrivacyLinkSigningKeyStartupTests
{
    private static WebApplicationFactory<Program> Factory(string key) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("AgentService:BaseUrl", "http://127.0.0.1:59999");
                builder.UseSetting("AgentService:InternalToken", "test-internal-token");
                builder.UseSetting("Privacy:LinkSigningKey", key);
            });

    [Fact]
    public void AShortSigningKeyRefusesToBoot()
    {
        using var factory = Factory(Convert.ToBase64String(new byte[16]));

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least 32 bytes*");
    }

    [Fact]
    public void AMalformedSigningKeyRefusesToBoot()
    {
        using var factory = Factory("this is not base64");

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*base64*");
    }

    [Fact]
    public void ABlankSigningKeyRefusesToBoot()
    {
        // An explicitly blank key is "configured to nothing": it is a mistake, unlike the absence
        // of the key, which is tolerated until the disclosure path is used.
        using var factory = Factory("");

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*blank*");
    }
}
