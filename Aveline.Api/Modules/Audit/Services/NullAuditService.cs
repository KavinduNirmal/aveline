using Aveline.Api.Modules.Audit.Models;

namespace Aveline.Api.Modules.Audit.Services;

/// <summary>
/// An <see cref="IAuditService"/> that records nothing. It exists so a service that wants to audit
/// can take the dependency unconditionally: the two unit-test call sites that construct the consent
/// service directly do not stand up the audit stack, and a nullable dependency would push a
/// null-check into every write.
/// </summary>
/// <remarks>
/// It is <b>not</b> registered by <c>AuditModule</c>; production always resolves the real
/// <see cref="AuditService"/>. Using it as the default is a testing/composition convenience, never a
/// production posture: if it is ever the resolved implementation, the audit trail is silently empty,
/// which is why the type name says so.
/// </remarks>
public sealed class NullAuditService : IAuditService
{
    /// <summary>The shared instance; the type is stateless.</summary>
    public static readonly NullAuditService Instance = new();

    /// <inheritdoc />
    public Task RecordAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
