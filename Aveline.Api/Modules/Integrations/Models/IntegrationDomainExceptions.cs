namespace Aveline.Api.Modules.Integrations.Models;

/// <summary>Raised when an integration has not been configured for the organization.</summary>
public sealed class IntegrationNotConfiguredException(IntegrationType type)
    : Exception($"The {type} integration is not configured for this organization.");

/// <summary>Raised when the supplied credentials are missing required fields for an integration type.</summary>
public sealed class InvalidIntegrationCredentialsException(IntegrationType type, string missing)
    : Exception($"The {type} credentials are missing required field(s): {missing}.");
