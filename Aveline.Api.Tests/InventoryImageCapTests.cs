using System.Net;
using System.Net.Http.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// Unit U0.6's gap, closed on lane L4's routes: the <c>/internal/visual</c> inventory write
/// endpoints reach the same <c>InventoryService.ProcessImageUrlAsync</c> as the catalog write
/// routes, so an over-cap <c>data:</c> image URL must get the catalog tier's one answer — a
/// <c>400</c> with the <c>{ error }</c> body — rather than falling through to the global
/// handler's <c>500</c>.
/// </summary>
/// <remarks>
/// <para>
/// The cap (<c>Media:CatalogMaxFileBytes</c>, strategy §3.4) is set to a deliberately small 1 KB
/// so the boundary is cheap to exercise and the test proves the behaviour reads the configured
/// key rather than a literal. The <see cref="IInventoryRepository"/> is a Moq spy: "nothing was
/// written" is asserted directly on it, with the in-memory <c>InventoryImages</c> table checked
/// as a backstop, so a refusal that is reported as a successful item write cannot pass.
/// </para>
/// <para>
/// Every route that reaches <c>ProcessImageUrlAsync</c> is covered, including the
/// <c>/api/internal/visual</c> and <c>/internal/inventory</c> compatibility aliases. All of them
/// share the two handler delegates in <c>VisualEndpoints.cs</c>, so the mapping is applied once
/// per delegate and this theory proves every address inherits it.
/// </para>
/// </remarks>
public class InventoryImageCapTests : IAsyncLifetime
{
    /// <summary>The test's <c>Media:CatalogMaxFileBytes</c>; small on purpose.</summary>
    private const int CapBytes = 1024;

    private const string InternalKey = "test-key";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Mock<IInventoryRepository> _repository = null!;

    public Task InitializeAsync()
    {
        _repository = new Mock<IInventoryRepository>();
        _repository
            .Setup(r => r.AddImageAsync(It.IsAny<InventoryImage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository
            .Setup(r => r.AddAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository
            .Setup(r => r.UpdateAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InventoryItem
            {
                Id = Guid.NewGuid(),
                ItemName = "Existing piece",
                Category = "saree",
                Color = "emerald",
                Status = "available"
            });

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "http://localhost:0");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:InternalToken", InternalKey);
                builder.UseSetting("Media:CatalogMaxFileBytes", CapBytes.ToString());

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IInventoryRepository>();
                    services.AddSingleton(_repository.Object);
                });
            });

        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ---------------------------------------------------------------------------------------
    // The two write handlers, across every registered address
    // ---------------------------------------------------------------------------------------

    public static TheoryData<string> CreateRoutes => new()
    {
        "/internal/visual/inventory",
        "/api/internal/visual/inventory",
        "/internal/inventory/"
    };

    public static TheoryData<string> UpdateRoutes => new()
    {
        "/internal/visual/inventory/{itemId}",
        "/api/internal/visual/inventory/{itemId}",
        "/internal/inventory/{itemId}"
    };

    [Theory]
    [MemberData(nameof(CreateRoutes))]
    public async Task CreateInventoryItem_OneByteOverCap_Returns400WithErrorBodyAndWritesNothing(string route)
    {
        var orgId = Guid.NewGuid();

        var response = await PostAsync(route, new
        {
            orgId,
            itemName = "Over-cap piece",
            category = "saree",
            color = "emerald",
            imageUrl = DataUrl(CapBytes + 1)
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await ReadErrorBodyAsync(response);
        body.Should().ContainSingle();
        body["error"].Should().Be(
            $"A catalog image may be at most {CapBytes} bytes; the payload was {CapBytes + 1} bytes.");

        _repository.Verify(
            r => r.AddImageAsync(It.IsAny<InventoryImage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(
            r => r.AddAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
        await AssertNoImageRowAsync(orgId);
    }

    [Theory]
    [MemberData(nameof(UpdateRoutes))]
    public async Task UpdateInventoryItem_OneByteOverCap_Returns400WithErrorBodyAndWritesNothing(string routeTemplate)
    {
        var itemId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var route = routeTemplate.Replace("{itemId}", itemId.ToString());

        var response = await PutAsync(route, new
        {
            orgId,
            itemName = "Over-cap update",
            imageUrl = DataUrl(CapBytes + 1)
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await ReadErrorBodyAsync(response);
        body.Should().ContainSingle();
        body["error"].Should().Be(
            $"A catalog image may be at most {CapBytes} bytes; the payload was {CapBytes + 1} bytes.");

        _repository.Verify(
            r => r.AddImageAsync(It.IsAny<InventoryImage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(
            r => r.UpdateAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
        await AssertNoImageRowAsync(orgId);
    }

    // ---------------------------------------------------------------------------------------
    // The boundary must not have moved: exactly the cap still succeeds
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateInventoryItem_ExactlyAtCap_SucceedsAndStoresImageAndItem()
    {
        var orgId = Guid.NewGuid();

        var response = await PostAsync("/internal/visual/inventory", new
        {
            orgId,
            itemName = "At-cap piece",
            category = "saree",
            color = "emerald",
            imageUrl = DataUrl(CapBytes)
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // The successful response shape is unchanged: the same InventoryItemDto contract.
        var created = await response.Content.ReadFromJsonAsync<InventoryItemDto>();
        created.Should().NotBeNull();
        created!.ItemName.Should().Be("At-cap piece");
        created.OrgId.Should().Be(orgId);
        created.ImageUrl.Should().StartWith($"/api/v1/orgs/{orgId}/catalog/images/");

        _repository.Verify(
            r => r.AddImageAsync(
                It.Is<InventoryImage>(i => i.ImageData != null && i.ImageData.LongLength == CapBytes),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(
            r => r.AddAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateInventoryItem_ExactlyAtCap_SucceedsAndStoresImageAndUpdatesItem()
    {
        var itemId = Guid.NewGuid();

        var response = await PutAsync($"/internal/visual/inventory/{itemId}", new
        {
            orgId = Guid.NewGuid(),
            itemName = "At-cap update",
            imageUrl = DataUrl(CapBytes)
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<InventoryItemDto>();
        updated.Should().NotBeNull();
        updated!.ItemName.Should().Be("At-cap update");
        updated.ImageUrl.Should().Contain("/catalog/images/");

        _repository.Verify(
            r => r.AddImageAsync(
                It.Is<InventoryImage>(i => i.ImageData != null && i.ImageData.LongLength == CapBytes),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(
            r => r.UpdateAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private async Task<HttpResponseMessage> PostAsync(string route, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PutAsync(string route, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, route)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);
        return await _client.SendAsync(request);
    }

    private static async Task<Dictionary<string, string>> ReadErrorBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body.Should().NotBeNull();
        body.Should().ContainKey("error");
        return body!;
    }

    private async Task AssertNoImageRowAsync(Guid orgId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Belt and braces with the spy: no row exists for the org under test either. The count is
        // org-scoped because every WebApplicationFactory in the process shares the app's single
        // named in-memory store ("AvelineInMemoryDb"), so a whole-table count would race with the
        // other test classes running in parallel.
        (await db.InventoryImages.CountAsync(i => i.OrgId == orgId)).Should().Be(0);
    }

    private static string DataUrl(int length)
    {
        var bytes = new byte[length];
        return "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
    }
}
