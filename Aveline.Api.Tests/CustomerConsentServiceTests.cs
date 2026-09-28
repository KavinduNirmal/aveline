using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 0 defect tests for the consent state machine (D-3, D-4, D-5) and for the single
/// absent-row wire value (D-2). The service and repository run against the in-memory EF
/// provider and the assertions read the persisted row, so a test is about stored state
/// rather than the shape of a response DTO.
/// </summary>
public class CustomerConsentServiceTests
{
    private readonly AppDbContext _context;
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly ICustomerConsentService _consent;
    private readonly ICustomerService _customers;

    public CustomerConsentServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"CustomerConsentSvc_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _consent = new CustomerConsentService(new CustomerConsentRepository(_context));
        _customers = new CustomerService(
            new CustomerRepository(_context),
            new CustomerConsentRepository(_context),
            new CustomerTagRepository(_context),
            new TestDistributedCache(),
            NullLogger<CustomerService>.Instance);
    }

    private async Task<Customer> SeedCustomerWithoutConsentRowAsync()
    {
        var customer = new Customer
        {
            OrganizationId = _orgId,
            PhoneNumber = "+94771234567",
            Status = "new",
        };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();
        return customer;
    }

    private Task<CustomerConsent?> RowAsync(Guid customerId) =>
        _context.CustomerConsents.FirstOrDefaultAsync(
            row => row.OrganizationId == _orgId && row.CustomerId == customerId);

    [Fact]
    public async Task Get_WithNoConsentRow_ReturnsPending()
    {
        // 0.2: an absent row means "identified, not yet answered" - which is `pending`.
        var customer = await SeedCustomerWithoutConsentRowAsync();

        var dto = await _consent.GetAsync(_orgId, customer.Id);

        Assert.Equal("pending", dto.ConsentStatus);
        Assert.Equal(Guid.Empty, dto.Id);
    }

    [Fact]
    public async Task Update_WithPending_IsAccepted()
    {
        // D-3: `pending` used to throw, so a withdrawn objection could not be represented.
        var customer = await SeedCustomerWithoutConsentRowAsync();

        var dto = await _consent.UpdateAsync(_orgId, customer.Id, "pending");

        Assert.Equal("pending", dto.ConsentStatus);
        var row = await RowAsync(customer.Id);
        Assert.NotNull(row);
        Assert.Equal("pending", row!.ConsentStatus);
    }

    [Fact]
    public async Task Update_ReGrant_ClearsConsentRevokedAt()
    {
        // D-4: the update branch never cleared RevokedAt, so a re-granted customer carried a
        // stale objection timestamp. The stale value is written directly, so this test is
        // about the re-grant and not about the insert branch (D-5).
        var customer = await SeedCustomerWithoutConsentRowAsync();
        await _consent.UpdateAsync(_orgId, customer.Id, "revoked");
        var seeded = await RowAsync(customer.Id);
        seeded!.ConsentRevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var dto = await _consent.UpdateAsync(_orgId, customer.Id, "granted");

        var row = await RowAsync(customer.Id);
        Assert.Equal("granted", row!.ConsentStatus);
        Assert.Null(row.ConsentRevokedAt);
        Assert.NotNull(row.ConsentGrantedAt);
        Assert.Equal("granted", dto.ConsentStatus);
    }

    [Fact]
    public async Task Update_Revoked_OnCustomerWithoutRow_StampsConsentRevokedAt()
    {
        // D-5: the insert branch set the status but no revocation timestamp, so a privacy audit
        // could not answer "when was the objection recorded?".
        var customer = await SeedCustomerWithoutConsentRowAsync();

        await _consent.UpdateAsync(_orgId, customer.Id, "revoked");

        var row = await RowAsync(customer.Id);
        Assert.NotNull(row);
        Assert.Equal("revoked", row!.ConsentStatus);
        Assert.NotNull(row.ConsentRevokedAt);
    }

    [Fact]
    public async Task Update_Granted_OnCustomerWithoutRow_StampsConsentGrantedAt()
    {
        // D-5, the affirmative half.
        var customer = await SeedCustomerWithoutConsentRowAsync();

        await _consent.UpdateAsync(_orgId, customer.Id, "granted");

        var row = await RowAsync(customer.Id);
        Assert.NotNull(row);
        Assert.Equal("granted", row!.ConsentStatus);
        Assert.NotNull(row.ConsentGrantedAt);
        Assert.Null(row.ConsentRevokedAt);
    }

    [Fact]
    public async Task Get_AndProfile_AgreeOnTheAbsentRowValue()
    {
        // D-2 / 0.2: the consent endpoint answered "pending" while the profile answered
        // "unknown" for the same absent row. Both call sites must report one value.
        var customer = await SeedCustomerWithoutConsentRowAsync();

        var consent = await _consent.GetAsync(_orgId, customer.Id);
        var profile = await _customers.GetProfileAsync(_orgId, customer.Id);

        Assert.NotNull(profile);
        Assert.Equal("pending", profile!.ConsentStatus);
        Assert.Equal(profile.ConsentStatus, consent.ConsentStatus);
    }

    [Fact]
    public async Task Update_UnknownStatus_ThrowsTheTypedConsentError()
    {
        // D-3 / 0.1: an unknown status is a domain error, not ArgumentException - the endpoint
        // maps the type to a documented 400 with a machine-readable code.
        var customer = await SeedCustomerWithoutConsentRowAsync();

        var exception = await Assert.ThrowsAsync<InvalidConsentStatusException>(
            () => _consent.UpdateAsync(_orgId, customer.Id, "bogus"));

        Assert.Equal("bogus", exception.Status);
        Assert.Equal("invalid-consent-status", exception.Code);
    }

    [Fact]
    public async Task Update_ReturnsTheWidenedConsentDto()
    {
        // 0.3: the DTO carries the timestamps the disclosure flow and staff UI read.
        var customer = await SeedCustomerWithoutConsentRowAsync();

        var dto = await _consent.UpdateAsync(_orgId, customer.Id, "revoked");

        Assert.Equal("revoked", dto.ConsentStatus);
        Assert.NotNull(dto.ConsentRevokedAt);
        Assert.Null(dto.ConsentGrantedAt);
        Assert.Null(dto.DisclosureShownAt);
    }

    [Fact]
    public async Task Get_ProjectsTheDisclosureColumnsOntoTheDto()
    {
        var customer = await SeedCustomerWithoutConsentRowAsync();
        var shownAt = DateTime.UtcNow;
        _context.CustomerConsents.Add(new CustomerConsent
        {
            OrganizationId = _orgId,
            CustomerId = customer.Id,
            ConsentStatus = "granted",
            ConsentGrantedAt = shownAt,
            DisclosureShownAt = shownAt,
            DisclosureVersion = "v1",
            ConsentSource = "api",
        });
        await _context.SaveChangesAsync();

        var dto = await _consent.GetAsync(_orgId, customer.Id);

        Assert.Equal(shownAt, dto.ConsentGrantedAt);
        Assert.Equal(shownAt, dto.DisclosureShownAt);
        Assert.Null(dto.ConsentRevokedAt);
    }
}
