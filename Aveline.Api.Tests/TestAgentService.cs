using System.Security.Cryptography;

namespace Aveline.Api.Tests;

/// <summary>
/// Shared values the integration hosts configure for the agent service.
/// </summary>
internal static class TestAgentService
{
    /// <summary>
    /// An internal service token that satisfies the Production startup guard introduced for F-2.6:
    /// at least 32 characters, at least 8 distinct characters, and not a value this repository
    /// publishes. Several test classes boot the host with <c>UseEnvironment("Production")</c> to
    /// assert Production-only behaviour, and those hosts now refuse to start with the short
    /// <c>test-internal-token</c> placeholder they used previously.
    ///
    /// <para>
    /// A literal rather than a random value so a failing host is reproducible and the token is
    /// greppable in logs; it is a test-only credential and never reaches a deployment.
    /// </para>
    /// </summary>
    public const string ProductionInternalToken =
        "n7Qv2mZx8LpR4tYbW1sK6dHgJ9aF3cVeU0iO5rNqT2wXzB8yD4hS1kM7gP6jC0lA";

    /// <summary>
    /// The short development token most test hosts use. Kept for the Development environment, where
    /// the Production guard does not apply - only a known published placeholder is refused.
    /// </summary>
    public const string DevelopmentInternalToken = "test-internal-token";

    /// <summary>
    /// Guards the assumption above: if the Production bar ever changes, this fails loudly here
    /// rather than as four unrelated integration failures.
    /// </summary>
    public static bool ProductionTokenMeetsTheBar() =>
        ProductionInternalToken.Length >= 32
        && ProductionInternalToken.Distinct().Count() >= 8;

    /// <summary>A random token, for tests that want a value with no history at all.</summary>
    public static string RandomInternalToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
