namespace Aveline.Api.Authorization;

/// <summary>
/// Permission names and the canonical role-to-permission grant catalog.
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

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        CatalogView,
        CustomersView,
        CatalogManage,
        ApprovalsApprove,
        PaymentsRefund,
        ReportsView,
        SettingsManage,
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RolePermissions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [Roles.Staff] = Grant(CatalogView),
            [Roles.CustomerRelations] = Grant(CatalogView, CustomersView),
            [Roles.Moderator] = Grant(CatalogView, CustomersView, ApprovalsApprove),
            [Roles.Admin] = All,
            [Roles.Owner] = All,

            [Roles.BoutiqueStaff] = Grant(CatalogView, CustomersView),
            [Roles.BoutiqueManager] = Grant(CatalogView, CustomersView, CatalogManage, ReportsView),
            [Roles.BoutiqueSupervisor] = Grant(
                CatalogView, CustomersView, CatalogManage, ApprovalsApprove, ReportsView),
            [Roles.BoutiqueOwner] = All,
        };

    /// <summary>Returns whether a canonical role has the requested permission.</summary>
    public static bool IsGranted(string role, string permission) =>
        RolePermissions.TryGetValue(role, out var grants) && grants.Contains(permission);

    private static IReadOnlySet<string> Grant(params string[] permissions) =>
        new HashSet<string>(permissions, StringComparer.Ordinal);
}
