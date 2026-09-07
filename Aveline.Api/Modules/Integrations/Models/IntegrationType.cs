using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.Integrations.Models;

/// <summary>
/// Third-party integrations a boutique owner supplies credentials for during
/// onboarding and manages from settings. Treated as string-backed enums across JSON
/// APIs and persistence (mirrors <see cref="Aveline.Api.Modules.Billing.Models.PlanTier"/>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IntegrationType
{
    /// <summary>Meta WhatsApp Business Platform (phone number messaging).</summary>
    WhatsApp,

    /// <summary>Meta Instagram / business messaging.</summary>
    Instagram,

    /// <summary>Payment gateway (PayHere / Stripe) used for boutique transactions.</summary>
    PaymentGateway,
}
