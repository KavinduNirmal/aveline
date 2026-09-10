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
    public const string ConversationsView = "conversations:view";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        CatalogView,
        CustomersView,
        CatalogManage,
        ApprovalsApprove,
        PaymentsRefund,
        ReportsView,
        SettingsManage,
        ConversationsView,
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RolePermissions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [Roles.Staff] = Grant(CatalogView, ConversationsView),
            [Roles.CustomerRelations] = Grant(CatalogView, CustomersView, ConversationsView),
            [Roles.Moderator] = Grant(CatalogView, CustomersView, ApprovalsApprove, ConversationsView),
            [Roles.Admin] = All,
            [Roles.Owner] = All,

            [Roles.BoutiqueStaff] = Grant(CatalogView, CustomersView, ConversationsView),
            [Roles.BoutiqueManager] = Grant(CatalogView, CustomersView, CatalogManage, ReportsView, ConversationsView),
            [Roles.BoutiqueSupervisor] = Grant(
                CatalogView, CustomersView, CatalogManage, ApprovalsApprove, ReportsView, ConversationsView),
            [Roles.BoutiqueOwner] = All,
        };

    /// <summary>Returns whether a canonical role has the requested permission.</summary>
    public static bool IsGranted(string role, string permission) =>
        RolePermissions.TryGetValue(role, out var grants) && grants.Contains(permission);

    private static IReadOnlySet<string> Grant(params string[] permissions) =>
        new HashSet<string>(permissions, StringComparer.Ordinal);
}
