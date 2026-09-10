using System.Text.Json;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

public class CustomerService : ICustomerService
{
    private const int MaxLookupResults = 5;
    private static readonly TimeSpan LookupCacheTtl = TimeSpan.FromSeconds(60);

    private readonly ICustomerRepository _customers;
    private readonly ICustomerConsentRepository _consent;
    private readonly ICustomerTagRepository _tags;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(
        ICustomerRepository customers,
        ICustomerConsentRepository consent,
        ICustomerTagRepository tags,
        IDistributedCache cache,
        ILogger<CustomerService> logger)
    {
        _customers = customers;
        _consent = consent;
        _tags = tags;
        _cache = cache;
        _logger = logger;
    }

    public async Task<CustomerProfileDto> IdentifyOrCreateAsync(
        Guid orgId,
        string phoneNumber,
        string? fullName = null,
        CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByPhoneAsync(orgId, phoneNumber, cancellationToken);
        if (customer is not null)
        {
            if (!string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(customer.FullName))
            {
                customer.FullName = fullName;
                await _customers.SaveAsync(customer, cancellationToken);
            }
            return await BuildProfileAsync(orgId, customer, cancellationToken);
        }

        var created = await _customers.AddAsync(new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = phoneNumber,
            FullName = fullName,
            Status = "new",
        }, cancellationToken);

        await _consent.AddAsync(new CustomerConsent
        {
            OrganizationId = orgId,
            CustomerId = created.Id,
            ConsentStatus = "pending",
        }, cancellationToken);

        return await BuildProfileAsync(orgId, created, cancellationToken);
    }

    public async Task<CustomerProfileDto?> GetProfileAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetAsync(orgId, customerId, cancellationToken);
        return customer is null ? null : await BuildProfileAsync(orgId, customer, cancellationToken);
    }

    public async Task<CustomerLookupResponse> LookupAsync(
        CustomerLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        // Read-only, so a short-lived cache keeps repeat lookups (e.g. the same name typed
        // several times) off the database. A cache failure is treated as a miss and never
        // fails the request (mirrors UserCacheService).
        var key = BuildLookupKey(request);
        var cached = await ReadCachedAsync(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var matches = await _customers.ListMatchesAsync(
            request.OrganizationId, request.Name, request.PhoneNumber, MaxLookupResults,
            cancellationToken, email: request.Email);

        var dto = new CustomerLookupResponse(
            matches.Select(CustomerMatchDto.From).ToList(),
            matches.Count == 1,
            matches.Count);

        await WriteCachedAsync(key, dto, cancellationToken);
        return dto;
    }

    private async Task<CustomerLookupResponse?> ReadCachedAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _cache.GetStringAsync(key, cancellationToken);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<CustomerLookupResponse>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer lookup cache read failed for key {Key}; treating as a miss.", key);
            return null;
        }
    }

    private async Task WriteCachedAsync(string key, CustomerLookupResponse response, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(response);
            await _cache.SetStringAsync(
                key,
                json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = LookupCacheTtl },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer lookup cache write failed for key {Key}.", key);
        }
    }

    private static string BuildLookupKey(CustomerLookupRequest request)
        => $"customer:lookup:{request.OrganizationId}:{request.Name?.Trim().ToLowerInvariant() ?? ""}:{request.PhoneNumber?.Trim() ?? ""}:{request.Email?.Trim().ToLowerInvariant() ?? ""}";

    private async Task<CustomerProfileDto> BuildProfileAsync(
        Guid orgId,
        Customer customer,
        CancellationToken cancellationToken)
    {
        var tags = await _tags.ListByCustomerAsync(orgId, customer.Id, cancellationToken);
        var consent = await _consent.GetForCustomerAsync(orgId, customer.Id, cancellationToken);
        return CustomerProfileDto.From(customer, tags, consent?.ConsentStatus ?? "unknown");
    }
}
