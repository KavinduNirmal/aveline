using System.Text.Json;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Commerce.Services;

public class BusinessRulesService : IBusinessRulesService
{
    private readonly IBusinessRulesRepository _repository;
    private readonly ILogger<BusinessRulesService> _logger;

    // Standard boutique business defaults
    private const decimal DefaultHighValueThreshold = 40000.00m; // LKR 40,000
    private const decimal DefaultMinMargin = 0.2500m;             // 25% minimum profit margin
    private const decimal DefaultVipDiscountCap = 0.1000m;        // 10% max for VIP
    private const decimal DefaultRegularDiscountCap = 0.0500m;    // 5% max for Regular
    private const decimal DefaultNewDiscountCap = 0.0000m;        // 0% for New customers

    public BusinessRulesService(
        IBusinessRulesRepository repository,
        ILogger<BusinessRulesService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<BusinessRuleResponseDto>> GetAllRulesAsync(
        Guid organizationId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var rules = await _repository.GetAllAsync(organizationId, activeOnly, cancellationToken);
        return rules.Select(MapToResponseDto).ToList();
    }

    public async Task<BusinessRuleResponseDto?> GetRuleByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var rule = await _repository.GetByIdAsync(id, organizationId, cancellationToken);
        return rule is null ? null : MapToResponseDto(rule);
    }

    public async Task<BusinessRuleResponseDto> CreateRuleAsync(
        Guid organizationId,
        CreateBusinessRuleDto dto,
        CancellationToken cancellationToken = default)
    {
        ValidateRuleValueJson(dto.RuleValue);

        var rule = new BusinessRule
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            RuleName = dto.RuleName.Trim(),
            RuleType = dto.RuleType.Trim().ToLowerInvariant(),
            RuleValue = dto.RuleValue.Trim(),
            Description = dto.Description?.Trim(),
            IsActive = dto.IsActive
        };

        var created = await _repository.CreateAsync(rule, cancellationToken);
        _logger.LogInformation("Business rule '{RuleName}' ({RuleType}) created for organization {OrgId}", created.RuleName, created.RuleType, organizationId);

        return MapToResponseDto(created);
    }

    public async Task<BusinessRuleResponseDto?> UpdateRuleAsync(
        Guid id,
        Guid organizationId,
        UpdateBusinessRuleDto dto,
        CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetByIdAsync(id, organizationId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(dto.RuleName))
        {
            existing.RuleName = dto.RuleName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.RuleValue))
        {
            ValidateRuleValueJson(dto.RuleValue);
            existing.RuleValue = dto.RuleValue.Trim();
        }

        if (dto.Description is not null)
        {
            existing.Description = dto.Description.Trim();
        }

        if (dto.IsActive.HasValue)
        {
            existing.IsActive = dto.IsActive.Value;
        }

        var updated = await _repository.UpdateAsync(existing, cancellationToken);
        _logger.LogInformation("Business rule {RuleId} updated for organization {OrgId}", id, organizationId);

        return MapToResponseDto(updated);
    }

    public async Task<bool> DeleteRuleAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _repository.DeleteAsync(id, organizationId, cancellationToken);
        if (deleted)
        {
            _logger.LogInformation("Business rule {RuleId} deleted for organization {OrgId}", id, organizationId);
        }
        return deleted;
    }

    public async Task<EvaluateOrderRulesResponseDto> EvaluateOrderRulesAsync(
        Guid organizationId,
        EvaluateOrderRulesRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var activeRules = await _repository.GetAllAsync(organizationId, activeOnly: true, cancellationToken);

        decimal highValueThreshold = DefaultHighValueThreshold;
        decimal minRequiredMargin = DefaultMinMargin;
        decimal maxAllowedDiscount = ResolveDefaultDiscountCap(request.CustomerTier);

        var flags = new List<string>();
        var triggeredRules = new List<string>();

        // Parse dynamic rules from database
        foreach (var rule in activeRules)
        {
            try
            {
                using var doc = JsonDocument.Parse(rule.RuleValue);
                var root = doc.RootElement;

                switch (rule.RuleType.ToLowerInvariant())
                {
                    case "approval_threshold":
                    case "high_value":
                        if (TryGetDecimalProperty(root, "threshold", out var thresholdVal) ||
                            TryGetDecimalProperty(root, "amount", out thresholdVal))
                        {
                            highValueThreshold = thresholdVal;
                        }
                        break;

                    case "min_margin":
                    case "margin":
                        if (TryGetDecimalProperty(root, "min_margin", out var marginVal) ||
                            TryGetDecimalProperty(root, "minMargin", out marginVal) ||
                            TryGetDecimalProperty(root, "margin", out marginVal))
                        {
                            minRequiredMargin = marginVal;
                        }
                        break;

                    case "discount":
                    case "loyalty_tier":
                        var tier = (request.CustomerTier ?? "regular").ToLowerInvariant();
                        if (TryGetDecimalProperty(root, $"{tier}_discount_cap", out var tierCap) ||
                            TryGetDecimalProperty(root, $"{tier}Discount", out tierCap) ||
                            TryGetDecimalProperty(root, "max_discount", out tierCap) ||
                            TryGetDecimalProperty(root, "maxDiscount", out tierCap))
                        {
                            maxAllowedDiscount = tierCap;
                        }
                        break;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse RuleValue JSON for rule {RuleId} ({RuleName})", rule.Id, rule.RuleName);
            }
        }

        // 1. High-Value Order Rule Check
        if (request.OrderTotal > highValueThreshold)
        {
            flags.Add($"Order total (LKR {request.OrderTotal:N2}) exceeds high-value threshold (LKR {highValueThreshold:N2})");
            triggeredRules.Add("HIGH_VALUE_THRESHOLD_EXCEEDED");
        }

        // 2. Discount Rule Check
        if (request.RequestedDiscount > maxAllowedDiscount)
        {
            flags.Add($"Requested discount ({request.RequestedDiscount * 100:0.#}%) exceeds max allowed discount ({maxAllowedDiscount * 100:0.#}%) for tier '{request.CustomerTier ?? "Regular"}'");
            triggeredRules.Add("DISCOUNT_LIMIT_EXCEEDED");
        }

        // 3. Minimum Margin Rule Check
        if (request.Margin < minRequiredMargin)
        {
            flags.Add($"Estimated margin ({request.Margin * 100:0.#}%) is below minimum required margin ({minRequiredMargin * 100:0.#}%)");
            triggeredRules.Add("LOW_MARGIN_THRESHOLD");
        }

        bool requiresApproval = flags.Count > 0;
        bool isAutoApproved = !requiresApproval;

        return new EvaluateOrderRulesResponseDto(
            RequiresApproval: requiresApproval,
            IsAutoApproved: isAutoApproved,
            MaxAllowedDiscount: maxAllowedDiscount,
            MinRequiredMargin: minRequiredMargin,
            HighValueThreshold: highValueThreshold,
            Flags: flags,
            TriggeredRules: triggeredRules
        );
    }

    private static decimal ResolveDefaultDiscountCap(string? tier)
    {
        return (tier?.Trim().ToLowerInvariant()) switch
        {
            "vip" => DefaultVipDiscountCap,
            "regular" => DefaultRegularDiscountCap,
            "new" => DefaultNewDiscountCap,
            _ => DefaultRegularDiscountCap
        };
    }

    private static bool TryGetDecimalProperty(JsonElement element, string propertyName, out decimal value)
    {
        value = 0m;
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out value))
                {
                    return true;
                }
                if (prop.Value.ValueKind == JsonValueKind.String && decimal.TryParse(prop.Value.GetString(), out value))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static void ValidateRuleValueJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON format for RuleValue: {ex.Message}", nameof(json), ex);
        }
    }

    private static BusinessRuleResponseDto MapToResponseDto(BusinessRule rule) =>
        new(
            rule.Id,
            rule.RuleName,
            rule.RuleType,
            rule.RuleValue,
            rule.IsActive,
            rule.Description,
            rule.CreatedAt,
            rule.UpdatedAt
        );
}
