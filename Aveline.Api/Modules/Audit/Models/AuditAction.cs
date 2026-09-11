namespace Aveline.Api.Modules.Audit.Models;

/// <summary>
/// Canonical audit action names. Constants (not an enum) because the value is stored
/// as a bounded varchar and new actions are added by new modules.
/// </summary>
public static class AuditAction
{
    public const string PricingRuleCreated = "pricing.rule.created";
    public const string PricingRuleUpdated = "pricing.rule.updated";
    public const string PricingRuleActivated = "pricing.rule.activated";
    public const string PricingRuleCancelled = "pricing.rule.cancelled";
    public const string PricingRuleRecomputed = "pricing.rule.recomputed";
    public const string PricingPriceEntryCreated = "pricing.price-entry.created";
    public const string PricingPriceEntryUpdated = "pricing.price-entry.updated";
    public const string PricingPriceEntryCancelled = "pricing.price-entry.cancelled";
    public const string ApiKeyCreated = "apikey.created";
    public const string ApiKeyRevoked = "apikey.revoked";
    public const string ApiKeyDeleted = "apikey.deleted";
    public const string OrganizationSettingsUpdated = "org.settings.updated";
    public const string MembershipRoleChanged = "membership.role.changed";
    public const string UserProfileUpdated = "user.profile.updated";
    public const string UserStateChanged = "user.state.changed";
}
