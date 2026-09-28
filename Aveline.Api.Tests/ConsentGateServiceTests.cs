using System.Diagnostics.Metrics;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Metrics;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Unit tests for the consent gate (plan §8.3 Layer 1, item 1.1). The gate is the one place the
/// inbound path asks "may this message be processed at all?", so its decision table is pinned
/// here rather than inferred from the call sites:
///
/// <list type="bullet">
/// <item>no customer ⇒ process (an unknown number cannot be consent-gated);</item>
/// <item><c>revoked</c> ⇒ skip;</item>
/// <item><c>granted</c>/<c>pending</c>/absent row ⇒ process;</item>
/// <item>repository failure ⇒ <b>fail closed</b> (do not process, and do not throw).</item>
/// </list>
/// </summary>
public class ConsentGateServiceTests
{
    private readonly AppDbContext _context;
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly ICustomerConsentRepository _consentRepository;
    private readonly IConsentGateService _gate;

    public ConsentGateServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ConsentGate_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _consentRepository = new CustomerConsentRepository(_context);
        _gate = new ConsentGateService(_consentRepository, NullLogger<ConsentGateService>.Instance);
    }

    private async Task<Guid> SeedCustomerAsync(string status)
    {
        var customer = new Customer
        {
            OrganizationId = _orgId,
            PhoneNumber = "+94771234567",
            Status = "new",
        };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        _context.CustomerConsents.Add(new CustomerConsent
        {
            OrganizationId = _orgId,
            CustomerId = customer.Id,
            ConsentStatus = status,
        });
        await _context.SaveChangesAsync();
        return customer.Id;
    }

    [Fact]
    public async Task Check_WithNoCustomer_Processes()
    {
        // An unknown number cannot be consent-gated, and the message must still be recorded
        // (plan §8.4). The decision is "process", with the reason surfaced for the skip counter.
        var decision = await _gate.CheckAsync(_orgId, customerId: null);

        Assert.True(decision.ShouldProcess);
        Assert.Equal(ConsentGateReasons.NoCustomerContext, decision.Reason);
    }

    [Fact]
    public async Task Check_WithRevokedConsent_Skips()
    {
        var customerId = await SeedCustomerAsync(ConsentStatuses.Revoked);

        var decision = await _gate.CheckAsync(_orgId, customerId);

        Assert.False(decision.ShouldProcess);
        Assert.Equal(ConsentStatuses.Revoked, decision.Status);
        Assert.Equal(ConsentGateReasons.ConsentRevoked, decision.Reason);
    }

    [Theory]
    [InlineData(ConsentStatuses.Granted)]
    [InlineData(ConsentStatuses.Pending)]
    public async Task Check_WithNonRevokedConsent_Processes(string status)
    {
        var customerId = await SeedCustomerAsync(status);

        var decision = await _gate.CheckAsync(_orgId, customerId);

        Assert.True(decision.ShouldProcess);
        Assert.Equal(status, decision.Status);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public async Task Check_WithNoConsentRow_ProcessesAsPending()
    {
        // D-2 / Pr0: an absent row is `pending`, not `unknown`, and `pending` is not a revocation.
        var customer = new Customer
        {
            OrganizationId = _orgId,
            PhoneNumber = "+94770000000",
            Status = "new",
        };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var decision = await _gate.CheckAsync(_orgId, customer.Id);

        Assert.True(decision.ShouldProcess);
        Assert.Equal(ConsentStatuses.Pending, decision.Status);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public async Task Check_WhenRepositoryFails_FailsClosedWithoutThrowing()
    {
        // Plan §8.3: failing open would process a potentially-revoked customer. A read failure
        // must therefore block the run - and must never surface as a 500.
        var gate = new ConsentGateService(
            new ThrowingConsentRepository(), NullLogger<ConsentGateService>.Instance);

        var decision = await gate.CheckAsync(_orgId, Guid.NewGuid());

        Assert.False(decision.ShouldProcess);
        Assert.Equal(ConsentGateReasons.ConsentCheckUnavailable, decision.Reason);
    }

    [Fact]
    public void RecordSkip_EmitsTheCounterLabelledByReason()
    {
        // message_skip_total{reason} is the plan's §9.1 processing counter. The label vocabulary is
        // asserted here because a counter whose reasons are free text is useless to alert on.
        var observed = new List<(string? Reason, long Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ConsentMetrics.MeterName
                && instrument.Name == ConsentMetrics.SkipMetricName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? reason = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "reason")
                {
                    reason = tag.Value?.ToString();
                }
            }
            observed.Add((reason, value));
        });
        listener.Start();

        using var metrics = new ConsentMetrics();
        metrics.RecordSkip(ConsentGateReasons.ConsentRevoked);
        metrics.RecordSkip(ConsentGateReasons.NoCustomerContext);
        metrics.RecordSkip(ConsentGateReasons.ConsentCheckUnavailable);
        listener.RecordObservableInstruments();

        Assert.Contains(observed, o => o is { Reason: "consent_revoked", Value: 1 });
        Assert.Contains(observed, o => o is { Reason: "no_customer_context", Value: 1 });
        Assert.Contains(observed, o => o is { Reason: "consent_check_unavailable", Value: 1 });
    }

    private sealed class ThrowingConsentRepository : ICustomerConsentRepository
    {
        public Task<CustomerConsent?> GetForCustomerAsync(
            Guid orgId, Guid customerId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("consent store unavailable");

        public Task<CustomerConsent> AddAsync(
            CustomerConsent consent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("consent store unavailable");

        public Task SaveAsync(CustomerConsent consent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("consent store unavailable");

        // The disclosure claim is not part of the gate's read path, but the interface carries it, so
        // the double must too. Throwing matches the "store unavailable" behaviour of its siblings.
        public Task<bool> TryClaimDisclosureAsync(
            Guid orgId,
            Guid customerId,
            string disclosureVersion,
            DateTime shownAt,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("consent store unavailable");

        public Task ReleaseDisclosureAsync(
            Guid orgId, Guid customerId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("consent store unavailable");
    }
}
