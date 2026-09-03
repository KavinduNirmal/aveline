namespace Aveline.Api.Authorization;

/// <summary>
/// Permission names and the role->permission catalog used by permission policies.
/// </summary>
public static class Permissions
{
    public const string CatalogView = "catalog:view";
    public const string CustomersView = "customers:view";
    public const string CatalogManage = "catalog:manage";
    public const string ApprovalsApprove = "approvals:approve";
    public const string PaymentsRefund = "payments:refund";
    public const string ReportsView = "reports:view";
    public const string SettingsManage = "settings:manage";

    /// <summary>Permission -> roles allowed to perform it.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> PermissionRoles =
        new Dictionary<string, string[]>
        {
            [CatalogView] = new[]
            {
                Roles.Associate, Roles.Manager, Roles.Owner,
                Roles.OrgAssociate, Roles.OrgManager, Roles.OrgOwner, Roles.OrgAdmin, Roles.OrgMember,
            },
            [CustomersView] = new[]
            {
                Roles.Associate, Roles.Manager, Roles.Owner,
                Roles.OrgAssociate, Roles.OrgManager, Roles.OrgOwner, Roles.OrgAdmin,
            },
            [CatalogManage] = new[]
            {
                Roles.Manager, Roles.Owner, Roles.OrgManager, Roles.OrgOwner, Roles.OrgAdmin,
            },
            [ApprovalsApprove] = new[]
            {
                Roles.Manager, Roles.Owner, Roles.OrgManager, Roles.OrgOwner, Roles.OrgAdmin,
            },
            [PaymentsRefund] = new[]
            {
                Roles.Owner, Roles.OrgOwner,
            },
            [ReportsView] = new[]
            {
                Roles.Manager, Roles.Owner, Roles.OrgManager, Roles.OrgOwner, Roles.OrgAdmin,
            },
            [SettingsManage] = new[]
            {
                Roles.Owner, Roles.OrgOwner,
            },
        };
}
