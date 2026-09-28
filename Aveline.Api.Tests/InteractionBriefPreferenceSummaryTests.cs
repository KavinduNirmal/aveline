using Aveline.Api.Modules.CustomerConcierge.DTOs;
using FluentAssertions;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// The interaction brief's <c>PreferenceSummary</c> is assembled from the customer's preference
/// rows. The derived <c>nickname</c> row is an internal mirror of <c>Customer.Nickname</c>, not a
/// preference the customer stated, and an empty value is an absence rather than a fact; both used
/// to reach the staff screen as <c>nickname: Nadi</c> and <c>color:</c>.
/// </summary>
public class InteractionBriefPreferenceSummaryTests
{
    private static CustomerProfileDto Profile(params CustomerPreferenceDto[] preferences) => new(
        Guid.NewGuid(),
        "+94771234567",
        null,
        "Nadia Client",
        "Prefers emerald silk",
        "returning",
        42000m,
        3,
        preferences,
        new List<string> { "vip" },
        "granted");

    private static CustomerPreferenceDto Preference(string key, string value) =>
        new(Guid.NewGuid(), key, value, true, 0.9m);

    [Fact]
    public void From_ExcludesTheInternalNicknameKeyAndBlankValues()
    {
        var profile = Profile(
            Preference("nickname", "Nadi"),
            Preference("fabric", "silk"),
            Preference("color", "   "));

        var brief = InteractionBriefDto.From(profile, Array.Empty<CustomerEventDto>());

        brief.PreferenceSummary.Should().Be(
            "fabric: silk",
            "the nickname is an internal key and a whitespace value is not a preference");
    }

    [Fact]
    public void From_WhenOnlyTheNicknameAndBlankValuesRemain_ReturnsNullSummary()
    {
        var profile = Profile(
            Preference("nickname", "Nadi"),
            Preference("budget", string.Empty));

        var brief = InteractionBriefDto.From(profile, Array.Empty<CustomerEventDto>());

        brief.PreferenceSummary.Should().BeNull(
            "nothing a customer stated remains, so there is no summary rather than an empty one");
    }

    [Fact]
    public void From_WithNoPreferencesAtAll_ReturnsNullSummary()
    {
        var brief = InteractionBriefDto.From(Profile(), Array.Empty<CustomerEventDto>());

        brief.PreferenceSummary.Should().BeNull();
    }

    [Fact]
    public void From_WithOrdinaryPreferences_JoinsThemInOrder()
    {
        var profile = Profile(
            Preference("fabric", "silk"),
            Preference("color", "emerald"));

        var brief = InteractionBriefDto.From(profile, Array.Empty<CustomerEventDto>());

        brief.PreferenceSummary.Should().Be("fabric: silk; color: emerald");
    }
}
