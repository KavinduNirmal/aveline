using Aveline.Api.Modules.Integrations.Models;

namespace Aveline.Api.Modules.Integrations.DTOs;

/// <summary>Payload supplied when saving credentials for an integration.</summary>
/// <param name="Credentials">
/// Key/value secrets for the integration (never plaintext-persisted). Required keys
/// differ by type: WhatsApp → <c>accessToken</c>; Instagram → <c>clientId</c>,
/// <c>clientSecret</c>, <c>accessToken</c>; PaymentGateway → <c>secretKey</c>.
/// </param>
/// <param name="Metadata">Optional non-sensitive JSON metadata (e.g. phone number, token expiry).</param>
public record SaveIntegrationRequest(
    IDictionary<string, string> Credentials,
    string? Metadata = null);

/// <summary>
/// Safe, non-secret view of a configured integration. Never carries plaintext secrets.
/// </summary>
public record IntegrationStatusDto(
    IntegrationType Type,
    IntegrationStatus Status,
    bool Connected,
    string? MaskedPreview,
    string? Metadata,
    DateTime? LastConnectedAt,
    string? LastError,
    DateTime UpdatedAt);

/// <summary>Result of a "Test &amp; Connect" attempt against a provider.</summary>
public record IntegrationTestResultDto(
    bool IsValid,
    string? Error,
    IntegrationStatusDto Status);

/// <summary>Safe view of a single inbound/outbound message log entry (no secrets).</summary>
public record InboundMessageLogDto(
    Guid Id,
    string Channel,
    string Direction,
    string? ExternalId,
    string? From,
    string? To,
    string? Content,
    DateTime ReceivedAt);
