using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Aveline.Api.Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Tests;

/// <summary>Issue #206 — Clerk session listing and revocation through the Backend API.</summary>
public class ClerkAdminClientTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                // The client disposes the request (and its content) after sending, so
                // the body has to be captured here rather than read by the test.
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return _responder(request);
        }
    }

    private static ClerkAdminClient CreateClient(ScriptedHandler handler, string? secretKey = "sk_test_secret")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Clerk:SecretKey"] = secretKey,
                ["Clerk:BackendApiUrl"] = "https://api.clerk.test/v1",
            })
            .Build();

        return new ClerkAdminClient(new HttpClient(handler), configuration);
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsParsedSessionsAndSendsTheSecret()
    {
        const string json = """
            [
              { "id": "sess_1", "status": "active", "created_at": 1700000000000, "last_active_at": 1700000100000, "expire_at": 1700000200000 },
              { "id": "sess_2", "status": "ended", "created_at": 1700000000000 }
            ]
            """;

        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        var sessions = await client.ListSessionsAsync("user_1");

        Assert.Equal(2, sessions.Count);
        Assert.Equal("sess_1", sessions[0].Id);
        Assert.Equal("active", sessions[0].Status);
        Assert.NotNull(sessions[0].CreatedAt);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        // `/sessions?user_id=…`, not `/users/{id}/sessions`. Clerk serves the latter far enough to
        // load the user and then answers with a plain-text `404 page not found` for a real id, which
        // the endpoint reports as a 502 for every caller (assessment F-4.5). The old assertion here
        // pinned that wrong path, so the suite stayed green throughout.
        Assert.Equal(
            "https://api.clerk.test/v1/sessions?user_id=user_1&limit=500",
            request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("sk_test_secret", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task ListSessionsAsync_OnRejection_NamesTheUrlItCalled()
    {
        // The 502 this raises is the only operator-visible signal, and it omitted the URL. A wrong
        // `Clerk:BackendApiUrl` answers with a plain-text `404 page not found` from a host that is
        // not Clerk's API, which reads as a Clerk outage — the live assessment settled on exactly
        // that wrong root cause (F-4.5 / OQ-4.1). Naming the URL makes it a one-line diagnosis.
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("404 page not found", Encoding.UTF8, "text/plain"),
        });
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ListSessionsAsync("user_9"));

        Assert.Contains("https://api.clerk.test/v1/sessions?user_id=user_9&limit=500", error.Message);
        Assert.Contains("404 page not found", error.Message);
    }

    [Fact]
    public async Task RevokeAllSessionsAsync_RevokesEveryActiveSession()
    {
        const string json = """
            [
              { "id": "sess_1", "status": "active" },
              { "id": "sess_2", "status": "active" },
              { "id": "sess_3", "status": "ended" }
            ]
            """;

        var handler = new ScriptedHandler(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        var revoked = await client.RevokeAllSessionsAsync("user_2");

        Assert.Equal(2, revoked);
        var revocations = handler.Requests.Where(r => r.Method == HttpMethod.Post).ToArray();
        Assert.Equal(2, revocations.Length);
        Assert.Contains(revocations, r => r.RequestUri!.ToString() == "https://api.clerk.test/v1/sessions/sess_1/revoke");
        Assert.Contains(revocations, r => r.RequestUri!.ToString() == "https://api.clerk.test/v1/sessions/sess_2/revoke");
    }

    [Fact]
    public async Task SessionCalls_WithoutASecret_FailFast()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler, secretKey: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListSessionsAsync("user_3"));
    }

    [Fact]
    public async Task GrantAdminRoleAsync_PatchesUserMetadata()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        await client.GrantAdminRoleAsync("user_4");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        // Clerk deprecated public_metadata on PATCH /users/{id}; metadata is written
        // through the dedicated /metadata endpoint.
        Assert.Equal("https://api.clerk.test/v1/users/user_4/metadata", request.RequestUri!.ToString());

        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("\"public_metadata\"", body);
        Assert.Contains("\"role\":\"admin\"", body);
    }

    [Fact]
    public async Task GrantAdminRoleAsync_OnRejection_ThrowsWithStatusCodeAndBody()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"errors\":[]}", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GrantAdminRoleAsync("user_5"));

        Assert.Contains("422", error.Message);
    }
}
