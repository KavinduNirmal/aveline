using System.Net;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.VisualIntelligence.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U1.2 (lane L2) — the catalog serve route's per-row dispatch (strategy §3.3; migration plan
/// §6.5): <c>GET …/catalog/images/{imageId}</c> answers <b>302</b> to the absolute delivery URL
/// for a <c>cloudinary</c> row, streams the bytes for a database row exactly as before, and
/// <b>404</b> when the row carries neither — never <c>500</c>.
/// </summary>
/// <remarks>
/// <para>
/// The dispatch is decided from the row alone, which is what makes a mixed population (stage 2 of
/// the staged rollout, strategy §5.2) work without a data migration. The tests seed the rows
/// directly, so they do not depend on the configured provider.
/// </para>
/// <para>
/// The client does not follow redirects: a followed <c>302</c> would leave the host for
/// <c>res.cloudinary.com</c>, which would be a network call in a unit-suite test.
/// </para>
/// </remarks>
public class CatalogImageRedirectTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ---------------------------------------------------------------------------------------
    // Per-row dispatch
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task GetImage_ForACloudinaryRow_Redirects302ToTheAbsoluteDeliveryUrl()
    {
        var image = await SeedAsync(row =>
        {
            row.StorageProvider = "cloudinary";
            row.StorageKey = $"image/upload:aveline/{row.OrgId}/catalog/{row.Id}";
            row.ImageData = null;
            row.ImageUrl = $"https://res.cloudinary.com/a-cloud/image/upload/w_800,f_auto,q_auto/v1/aveline/{row.OrgId}/catalog/{row.Id}.jpg";
        });

        var response = await _client.GetAsync(Route(image));

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Be(image.ImageUrl);
    }

    [Fact]
    public async Task GetImage_ForACloudinaryRow_IsAnonymous()
    {
        // F-7/Q4: the route stays deliberately anonymous, so the redirect must not require a
        // bearer token. This is the runtime half of the F-7 guard.
        var image = await SeedAsync(row =>
        {
            row.StorageProvider = "cloudinary";
            row.StorageKey = $"image/upload:aveline/{row.OrgId}/catalog/{row.Id}";
            row.ImageUrl = $"https://res.cloudinary.com/a-cloud/image/upload/w_800,f_auto,q_auto/v1/aveline/{row.OrgId}/catalog/{row.Id}.jpg";
        });

        var response = await _client.GetAsync(Route(image));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().Be(HttpStatusCode.Found);
    }

    [Fact]
    public async Task GetImage_ForADatabaseRow_ServesTheBytesExactlyAsBefore()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x4A, 0x46 };
        var image = await SeedAsync(row =>
        {
            row.StorageProvider = "database";
            row.StorageKey = null;
            row.ImageData = bytes;
            row.ContentType = "image/jpeg";
            row.ImageUrl = $"/api/v1/orgs/{row.OrgId}/catalog/images/{row.Id}";
        });

        var response = await _client.GetAsync(Route(image));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
    }

    [Fact]
    public async Task GetImage_WhenTheRowCarriesNeither_Returns404()
    {
        var image = await SeedAsync(row =>
        {
            row.StorageProvider = "database";
            row.StorageKey = null;
            row.ImageData = null;
        });

        var response = await _client.GetAsync(Route(image));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetImage_ForAMissingRow_Returns404()
    {
        var response = await _client.GetAsync(
            $"/api/v1/orgs/{Guid.NewGuid()}/catalog/images/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetImage_ForACloudinaryRowWithAnUnusableUrl_Is404Never500()
    {
        // A cloudinary row whose stored URL cannot be redirected to must degrade to the bytes
        // branch (none here) and then 404 — the route may never 500 on row shape.
        var image = await SeedAsync(row =>
        {
            row.StorageProvider = "cloudinary";
            row.StorageKey = $"image/upload:aveline/{row.OrgId}/catalog/{row.Id}";
            row.ImageData = null;
            row.ImageUrl = string.Empty;
        });

        var response = await _client.GetAsync(Route(image));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetImage_ForACloudinaryRowWithDualWriteBytes_StillRedirects()
    {
        // The dispatch is by provider, not by which copy happens to exist (strategy §3.3).
        var image = await SeedAsync(row =>
        {
            row.StorageProvider = "cloudinary";
            row.StorageKey = $"image/upload:aveline/{row.OrgId}/catalog/{row.Id}";
            row.ImageData = [1, 2, 3];
            row.ImageUrl = $"https://res.cloudinary.com/a-cloud/image/upload/w_800,f_auto,q_auto/v1/aveline/{row.OrgId}/catalog/{row.Id}.jpg";
        });

        var response = await _client.GetAsync(Route(image));

        response.StatusCode.Should().Be(HttpStatusCode.Found);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static string Route(InventoryImage image) =>
        $"/api/v1/orgs/{image.OrgId}/catalog/images/{image.Id}";

    private async Task<InventoryImage> SeedAsync(Action<InventoryImage> configure)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var image = new InventoryImage
        {
            Id = Guid.NewGuid(),
            OrgId = Guid.NewGuid(),
            ContentType = "image/jpeg",
            FileName = "seeded.jpg",
            FileSizeBytes = 6,
            ImageUrl = string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
        };
        configure(image);

        db.InventoryImages.Add(image);
        await db.SaveChangesAsync();
        return image;
    }
}
