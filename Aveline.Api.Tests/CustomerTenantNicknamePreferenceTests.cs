using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// A blank nickname means "no nickname". The update path used to keep the derived
/// <c>nickname</c> preference row and blank its value, and the brief rendered the leftover row as
/// <c>nickname:</c>. The create path already refused to write a row for a blank nickname; these
/// tests pin the update path to the same end state.
/// </summary>
public class CustomerTenantNicknamePreferenceTests
{
    private static AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"NicknamePref_{Guid.NewGuid()}")
            .Options);

    private static CustomerTenantService BuildService(AppDbContext context) =>
        new(context, Mock.Of<IConversationService>());

    private static UpdateCustomerRequest Nickname(string? value) =>
        new(null, value, null, null, null, null);

    [Fact]
    public async Task UpdateAsync_WhenNicknameBecomesBlank_RemovesTheDerivedNicknamePreferenceRow()
    {
        var orgId = Guid.NewGuid();
        await using var context = NewContext();

        var customer = new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = "+94771234567",
            FullName = "Nadia Client",
            Nickname = "Nadi",
            Status = "new",
        };
        context.Customers.Add(customer);
        context.CustomerPreferences.Add(new CustomerPreference
        {
            OrganizationId = orgId,
            CustomerId = customer.Id,
            PreferenceKey = "nickname",
            PreferenceValue = "Nadi",
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var updated = await BuildService(context).UpdateAsync(orgId, customer.Id, Nickname("   "));

        updated.Should().NotBeNull();
        updated!.Nickname.Should().BeNull("a whitespace nickname is no nickname");

        var rows = await context.CustomerPreferences
            .AsNoTracking()
            .Where(row => row.CustomerId == customer.Id)
            .ToListAsync();

        rows.Should().NotContain(
            row => row.PreferenceKey == "nickname",
            "the derived row must be removed, not blanked: PreferenceValue is required, and the "
            + "brief rendered the empty value as `nickname:`");
        rows.Should().NotContain(row => string.IsNullOrWhiteSpace(row.PreferenceValue));
    }

    [Fact]
    public async Task UpdateAsync_WhenNicknameBecomesBlankAndNoRowExists_AddsNothing()
    {
        var orgId = Guid.NewGuid();
        await using var context = NewContext();

        var customer = new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = "+94771234567",
            FullName = "Nadia Client",
            Status = "new",
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var updated = await BuildService(context).UpdateAsync(orgId, customer.Id, Nickname(" "));

        updated.Should().NotBeNull();
        updated!.Nickname.Should().BeNull();
        (await context.CustomerPreferences.CountAsync()).Should().Be(
            0, "a blank nickname must never create an empty preference row");
    }

    [Fact]
    public async Task UpdateAsync_WithANonBlankNickname_StillUpsertsTheDerivedRow()
    {
        var orgId = Guid.NewGuid();
        await using var context = NewContext();

        var customer = new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = "+94771234567",
            FullName = "Nadia Client",
            Nickname = "Nadi",
            Status = "new",
        };
        context.Customers.Add(customer);
        context.CustomerPreferences.Add(new CustomerPreference
        {
            OrganizationId = orgId,
            CustomerId = customer.Id,
            PreferenceKey = "nickname",
            PreferenceValue = "Nadi",
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var updated = await BuildService(context).UpdateAsync(orgId, customer.Id, Nickname("  Nadi P  "));

        updated.Should().NotBeNull();
        updated!.Nickname.Should().Be("Nadi P");

        var row = await context.CustomerPreferences
            .AsNoTracking()
            .SingleAsync(row => row.CustomerId == customer.Id && row.PreferenceKey == "nickname");
        row.PreferenceValue.Should().Be("Nadi P");
    }

    [Fact]
    public async Task UpdateAsync_WithNoNicknameInTheRequest_LeavesTheExistingNicknameAlone()
    {
        var orgId = Guid.NewGuid();
        await using var context = NewContext();

        var customer = new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = "+94771234567",
            FullName = "Nadia Client",
            Nickname = "Nadi",
            Status = "new",
        };
        context.Customers.Add(customer);
        context.CustomerPreferences.Add(new CustomerPreference
        {
            OrganizationId = orgId,
            CustomerId = customer.Id,
            PreferenceKey = "nickname",
            PreferenceValue = "Nadi",
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // `Nickname` is null, which is "not touched" rather than "cleared".
        var updated = await BuildService(context).UpdateAsync(orgId, customer.Id, Nickname(null));

        updated.Should().NotBeNull();
        updated!.Nickname.Should().Be("Nadi");
        (await context.CustomerPreferences.CountAsync(row => row.CustomerId == customer.Id))
            .Should().Be(1);
    }
}
