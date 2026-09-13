namespace Aveline.Api.Modules.Statistics.DTOs;

/// <summary>
/// The validated filter shared by every API statistics query. <see cref="OrganizationId"/>
/// is <c>null</c> only for the team-only admin view; org-scoped callers always set it, since
/// there is no EF global tenant filter (constraint C-3).
/// </summary>
public sealed record ApiStatisticsFilter(
    Guid? OrganizationId,
    DateTime From,
    DateTime To,
    string? RouteTemplate = null,
    string? HttpMethod = null,
    short? StatusCode = null,
    string? StatusClass = null,
    Guid? ApiKeyId = null,
    Guid? UserId = null,
    int Page = 1,
    int PageSize = 50,
    string? GroupBy = null);
