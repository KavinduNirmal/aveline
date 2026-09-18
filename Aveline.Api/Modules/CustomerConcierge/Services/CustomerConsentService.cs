using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

public class CustomerConsentService : ICustomerConsentService
{
    private readonly ICustomerConsentRepository _consent;

    public CustomerConsentService(ICustomerConsentRepository consent) => _consent = consent;

    public async Task<CustomerConsentDto> GetAsync(Guid orgId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var row = await _consent.GetForCustomerAsync(orgId, customerId, cancellationToken);
        return row is null
            ? new CustomerConsentDto(Guid.Empty, "pending")
            : CustomerConsentDto.From(row);
    }

    public async Task<CustomerConsentDto> UpdateAsync(
        Guid orgId,
        Guid customerId,
        string status,
        CancellationToken cancellationToken = default)
    {
        var normalized = status.ToLowerInvariant() switch
        {
            "granted" => "granted",
            "revoked" => "revoked",
            _ => throw new ArgumentException($"Unsupported consent status: {status}"),
        };

        var row = await _consent.GetForCustomerAsync(orgId, customerId, cancellationToken);
        if (row is null)
        {
            row = new CustomerConsent
            {
                OrganizationId = orgId,
                CustomerId = customerId,
                ConsentStatus = normalized,
            };
            row = await _consent.AddAsync(row, cancellationToken);
        }
        else
        {
            row.ConsentStatus = normalized;
            row.ConsentGrantedAt = normalized == "granted" ? DateTime.UtcNow : row.ConsentGrantedAt;
            row.ConsentRevokedAt = normalized == "revoked" ? DateTime.UtcNow : row.ConsentRevokedAt;
            await _consent.SaveAsync(row, cancellationToken);
        }

        return CustomerConsentDto.From(row);
    }
}
