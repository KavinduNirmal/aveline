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
    // ---- The API's HTTP verbs (what the dashboard sends) --------------------------------

    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Revise = "revise";

    // ---- The agent's vocabulary (what the graph routes on) ------------------------------
    //
    // Two vocabularies exist because the dashboard's verbs are imperative and the graph's are
    // states. Nothing used to compare them: the API sent "approve" while the graph matched
    // "approved", so a resume fell through every branch and settled nothing (ADR-024, Decision 3).
    // The graph's values are the source of truth; `ToAgentDecision` is the single translation
    // point, and the tests pin these literals against the Python enum so they cannot drift apart
    // again.

    /// <summary>What the agent graph records when the owner accepts a paused deal.</summary>
    public const string AgentApproved = "approved";

    /// <summary>What the agent graph records when the owner declines a paused deal.</summary>
    public const string AgentRejected = "rejected";

    /// <summary>What the agent graph records when the owner changes the terms and re-approves.</summary>
    public const string AgentRevised = "revised";

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

    /// <summary>
    /// Translate an HTTP verb into the vocabulary the agent graph routes on.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> for anything unrecognised rather than defaulting: guessing a decision
    /// would settle a pause that no human actually decided.
    /// </remarks>
    public static string? ToAgentDecision(string? decision)
    {
        if (string.IsNullOrWhiteSpace(decision))
        {
            return null;
        }

        return decision.Trim().ToLowerInvariant() switch
        {
            Approve => AgentApproved,
            Reject => AgentRejected,
            Revise => AgentRevised,
            _ => null,
        };
    }
}
