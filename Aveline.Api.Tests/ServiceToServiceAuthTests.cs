using System.Net;
using System.Text;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Tests;

/// <summary>
/// Verifies the service-to-service auth handler (#18) adds the internal token header.
/// </summary>
public class InternalServiceAuthHandlerTests
{
    [Fact]
    public async Task Adds_Internal_Token_Header_To_Request()
    {
        var handler = new InternalServiceAuthHandler(ConfigFor("secret-token"))
        {
            InnerHandler = new CapturingHttpMessageHandler(),
        };

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://agent.local/ping");

        await client.SendAsync(request);

        Assert.Equal(
            "secret-token",
            request.Headers.GetValues(InternalServiceAuthHandler.HeaderName).Single());
    }

    [Fact]
    public void Throws_When_Token_Not_Configured()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new InternalServiceAuthHandler(ConfigFor(string.Empty)));
    }

    private static IConfiguration ConfigFor(string token) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentService:InternalToken"] = token,
            })
            .Build();
}

/// <summary>
/// Verifies the typed agent client routes requests to the configured base address.
/// </summary>
public class AgentServiceClientTests
{
    [Fact]
    public async Task PostAsync_Forwards_Path_Against_BaseAddress()
    {
        var capture = new CapturingHttpMessageHandler();
        using var http = new HttpClient(capture) { BaseAddress = new Uri("http://agent.local/") };
        var client = new AgentServiceClient(http);

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        await client.PostAsync("/agents/ping", content);

        Assert.Equal(HttpMethod.Post, capture.LastRequest?.Method);
        Assert.Equal("http://agent.local/agents/ping", capture.LastRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetAsync_Forwards_Path_Against_BaseAddress()
    {
        var capture = new CapturingHttpMessageHandler();
        using var http = new HttpClient(capture) { BaseAddress = new Uri("http://agent.local/") };
        var client = new AgentServiceClient(http);

        await client.GetAsync("/agents/ping");

        Assert.Equal(HttpMethod.Get, capture.LastRequest?.Method);
        Assert.Equal("http://agent.local/agents/ping", capture.LastRequest?.RequestUri?.ToString());
    }
}

/// <summary>
/// Verifies <see cref="ServiceClientsConfiguration.AddAgentServiceClient"/> registers
/// the typed agent client from configuration.
/// </summary>
public class ServiceClientsConfigurationTests
{
    [Fact]
    public void Registers_AgentServiceClient()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentService:BaseUrl"] = "http://agent.local/",
                ["AgentService:InternalToken"] = "secret-token",
            })
            .Build();

        var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddAgentServiceClient(config)
            .BuildServiceProvider();

        var client = provider.GetRequiredService<IAgentServiceClient>();

        Assert.IsType<AgentServiceClient>(client);
    }

    [Fact]
    public void Throws_When_BaseUrl_Not_Configured()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddAgentServiceClient(config));
    }
}
