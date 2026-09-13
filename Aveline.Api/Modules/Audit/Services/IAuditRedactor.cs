namespace Aveline.Api.Modules.Audit.Services;

/// <summary>
/// Strips secrets and sensitive payload content before an audit snapshot is persisted.
/// </summary>
public interface IAuditRedactor
{
    /// <summary>
    /// Serializes <paramref name="payload"/> to JSON and replaces the value of every
    /// sensitive key with a marker. Returns <c>null</c> for a null payload.
    /// </summary>
    string? Redact(object? payload);
}
