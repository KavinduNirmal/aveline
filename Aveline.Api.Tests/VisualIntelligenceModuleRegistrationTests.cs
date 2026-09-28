using System.Reflection;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.VisualIntelligence;
using Aveline.Api.Modules.VisualIntelligence.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// The catalog image-analysis path is the only place the boutique spends Blossoms without going
/// through the agent service, so the usage tracker it reports to must actually be wired by the
/// container.
/// </summary>
/// <remarks>
/// <see cref="VisionServiceTests"/> already asserts that <see cref="VisionService"/> reports usage
/// when a tracker is handed to its constructor - but it hands one in itself, so it cannot see a
/// container that never injects one. <see cref="VisionService"/>'s tracker parameter is optional
/// (<c>IUsageTrackerService? usageTracker = null</c>), so a missing registration degrades to
/// "analysis succeeds, nothing consumed" with no error anywhere. These tests resolve through the
/// real module registration instead.
/// </remarks>
public class VisualIntelligenceModuleRegistrationTests
{
    [Fact]
    public void ResolvedVisionService_ReceivesTheUsageTracker_SoAnalysisConsumesBlossoms()
    {
        using var scope = BuildProvider().CreateScope();

        var vision = scope.ServiceProvider.GetRequiredService<IVisionService>();
        UsageTrackerOf(vision).Should().NotBeNull(
            "the catalog analyze-image path must consume Blossoms (ADR-010); a null tracker "
            + "silently records nothing while still paying the provider");
    }

    [Fact]
    public void ResolvedVisionService_IsTheTypedClient_NotASecondRegistration()
    {
        // AddHttpClient<IVisionService, VisionService> uses a typed-client factory, so the
        // container - not `new` - chooses the constructor. A plain AddScoped<IVisionService,
        // VisionService> added later would be the last registration and would win silently.
        using var scope = BuildProvider().CreateScope();

        var vision = scope.ServiceProvider.GetRequiredService<IVisionService>();

        vision.Should().BeOfType<VisionService>();
        UsageTrackerOf(vision).Should().NotBeNull();
    }

    private static object? UsageTrackerOf(IVisionService vision)
    {
        var field = typeof(VisionService).GetField(
            "_usageTracker", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull("VisionService must keep the tracker it was constructed with");
        return field!.GetValue(vision);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // A stand-in for BillingModule's registration: the point is only that *a* tracker is
        // resolvable when the vision client is activated.
        services.AddScoped<IUsageTrackerService>(_ => Mock.Of<IUsageTrackerService>());
        services.AddVisualIntelligenceModule();
        return services.BuildServiceProvider();
    }
}
