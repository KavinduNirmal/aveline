namespace Aveline.Api.Authorization;

/// <summary>
/// Permission names and the canonical role-to-permission grant catalog.
/// </summary>
/// <remarks>
/// This catalog is the single source of truth for authorization. Adding a permission to
/// <see cref="All"/> automatically registers a matching policy (see
/// <c>AuthorizationConfiguration.AddAvelineAuthorization</c>) and
/// <c>PermissionsCatalogTests</c> enforces that every permission has at least one role
/// grant. Boutique roles deliberately never hold money-shaped permissions:
/// <c>pricing:manage</c>, <c>pricing:backdate</c>, <c>billing:adjust</c>,
/// <c>stats:system</c>, <c>admin:*</c> and <c>audit:view</c>.
/// </remarks>
public static class Permissions
{
    // Existing catalog.
    public const string CatalogView = "catalog:view";
    public const string CustomersView = "customers:view";
    public const string CatalogManage = "catalog:manage";
    public const string ApprovalsApprove = "approvals:approve";
    public const string PaymentsRefund = "payments:refund";
    public const string ReportsView = "reports:view";
    public const string SettingsManage = "settings:manage";
    public const string ConversationsView = "conversations:view";

    // Billing and pricing.
    public const string BillingView = "billing:view";

    /// <summary>
    /// The self-service read of a shop's Blossom position. Distinct from
    /// <see cref="BillingView"/> so that the associate who needs to know how many
    /// Blossoms the shop has left does not thereby become a reader of usage
    /// statements and burn-rate; both are backed by the same balance service.
    /// </summary>
    public const string BillingViewSelf = "billing:view:self";
    public const string BillingManage = "billing:manage";
    public const string BillingAdjust = "billing:adjust";
    public const string PricingView = "pricing:view";
    public const string PricingManage = "pricing:manage";
    public const string PricingBackdate = "pricing:backdate";

    // API access.
    public const string ApiKeysView = "apikeys:view";
    public const string ApiKeysManage = "apikeys:manage";

    // Statistics.
    public const string StatsView = "stats:view";
    public const string StatsViewAgent = "stats:view:agent";
    public const string StatsSystem = "stats:system";

    /// <summary>
    /// The administrator console's business-KPI surface: growth, active users, plan mix,
    /// subscription trend and platform-wide usage. Distinct from <see cref="StatsSystem"/>,
    /// which is about system health: a moderator already reads organization data through
    /// <see cref="AdminOrgsRead"/> and <see cref="BillingView"/>, so growth is inside their
    /// remit without granting system-health access.
    /// </summary>
    public const string AnalyticsBusinessRead = "analytics:business:read";

    // Aveline-team administration.
    public const string AdminUsersRead = "admin:users:read";
    public const string AdminUsersManage = "admin:users:manage";
    public const string AdminOrgsRead = "admin:orgs:read";
    public const string AuditView = "audit:view";

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
        BillingView,
        BillingViewSelf,
        BillingManage,
        BillingAdjust,
        PricingView,
        PricingManage,
        PricingBackdate,
        ApiKeysView,
        ApiKeysManage,
        StatsView,
        StatsViewAgent,
        StatsSystem,
        AnalyticsBusinessRead,
        AdminUsersRead,
        AdminUsersManage,
        AdminOrgsRead,
        AuditView,
    };

    private static readonly string[] CanonicalRoles =
    [
        Roles.Staff,
        Roles.CustomerRelations,
        Roles.Moderator,
        Roles.Admin,
        Roles.Owner,
        Roles.BoutiqueStaff,
        Roles.BoutiqueManager,
        Roles.BoutiqueSupervisor,
        Roles.BoutiqueOwner,
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RolePermissions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [Roles.Staff] = Grant(CatalogView, ConversationsView),
            [Roles.CustomerRelations] = Grant(CatalogView, CustomersView, ConversationsView),
            [Roles.Moderator] = Grant(
                CatalogView, CustomersView, ApprovalsApprove, ConversationsView,
                BillingView, StatsView, StatsViewAgent, AdminOrgsRead, AnalyticsBusinessRead),
            [Roles.Admin] = Grant(All.Where(permission => permission != PricingBackdate).ToArray()),
            [Roles.Owner] = All,

            [Roles.BoutiqueStaff] = Grant(CatalogView, CustomersView, ConversationsView, BillingViewSelf),
            [Roles.BoutiqueManager] = Grant(
                CatalogView, CustomersView, CatalogManage, ReportsView, ConversationsView,
                BillingView, BillingViewSelf, PricingView, StatsView),
            [Roles.BoutiqueSupervisor] = Grant(
                CatalogView, CustomersView, CatalogManage, ApprovalsApprove, ReportsView, ConversationsView,
                BillingViewSelf, StatsView),
            [Roles.BoutiqueOwner] = Grant(
                CatalogView, CustomersView, CatalogManage, ApprovalsApprove, PaymentsRefund,
                ReportsView, SettingsManage, ConversationsView,
                BillingView, BillingViewSelf, BillingManage, PricingView, ApiKeysView, ApiKeysManage,
                StatsView, StatsViewAgent),
        };

    /// <summary>Returns whether a canonical role has the requested permission.</summary>
    public static bool IsGranted(string role, string permission) =>
        RolePermissions.TryGetValue(role, out var grants) && grants.Contains(permission);

    /// <summary>
    /// The canonical roles that grant <paramref name="permission"/>, in catalog order.
    /// Used by the permission-catalog tests and by documentation generation.
    /// </summary>
    public static IReadOnlyList<string> RolesGranting(string permission) =>
        CanonicalRoles.Where(role => IsGranted(role, permission)).ToArray();

    private static IReadOnlySet<string> Grant(params string[] permissions) =>
        new HashSet<string>(permissions, StringComparer.Ordinal);
}
