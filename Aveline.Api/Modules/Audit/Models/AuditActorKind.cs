namespace Aveline.Api.Modules.Audit.Models;

/// <summary>Who performed an audited action (domain-model.md §9).</summary>
public static class AuditActorKind
{
    public const string User = "User";
    public const string ApiKey = "ApiKey";
    public const string InternalService = "InternalService";
    public const string System = "System";
}
