namespace Aveline.Api.Modules.Commerce.Models;

/// <summary>
/// Classifies an approval decision by what it does to the order — which is what decides the
/// permission it needs.
/// </summary>
/// <remarks>
/// Q14/R-17. `ApprovalsController` originally put one policy on all four verbs, so granting a staff
/// member `approvals:approve` also granted them `reject` (which **cancels** the order) and `revise`
/// (which **rewrites** its discount, total and margin). The answer to Q8 was explicit — staff may
/// approve customer orders but may not change order lifecycle — so the split has to be by **verb**,
/// not by route: `POST /decision` carries the verb in its body, and a route-level policy alone would
/// leave that door open.
/// </remarks>
public static class ApprovalDecisions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Revise = "revise";

    /// <summary>
    /// True for the decisions that cancel the order or rewrite its money fields, and therefore
    /// require <c>orders:manage</c> rather than <c>approvals:approve</c>.
    /// </summary>
    public static bool RequiresOrderManage(string? decision)
    {
        if (string.IsNullOrWhiteSpace(decision))
        {
            return false;
        }

        return decision.Trim().ToLowerInvariant() is Reject or Revise;
    }
}
