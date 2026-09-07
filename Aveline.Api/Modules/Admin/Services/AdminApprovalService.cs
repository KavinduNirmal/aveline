using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Admin.Models;
using Aveline.Api.Modules.Admin.Repositories;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Admin.Services;

/// <summary>
/// Administrator access request lifecycle. Approval grants the <c>admin</c> team
/// role through the Clerk Backend API and updates the local read model so the next
/// (refreshed) session token carries <c>user_role=admin</c>.
/// </summary>
public class AdminApprovalService : IAdminApprovalService
{
    private readonly IAdminApprovalRepository _repository;
    private readonly IClerkAdminClient _clerkAdminClient;
    private readonly IUserRepository _userRepository;
    private readonly IUserCacheService _cacheService;
    private readonly ILogger<AdminApprovalService> _logger;

    public AdminApprovalService(
        IAdminApprovalRepository repository,
        IClerkAdminClient clerkAdminClient,
        IUserRepository userRepository,
        IUserCacheService cacheService,
        ILogger<AdminApprovalService> logger)
    {
        _repository = repository;
        _clerkAdminClient = clerkAdminClient;
        _userRepository = userRepository;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<AdminApprovalRequest> SubmitAsync(
        SubmitAdminRequestInput input,
        CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetByClerkUserIdAsync(input.ClerkUserId, cancellationToken);
        if (existing is { Status: AdminApprovalStatus.Pending or AdminApprovalStatus.Approved })
        {
            return existing;
        }

        var request = new AdminApprovalRequest
        {
            ClerkUserId = input.ClerkUserId,
            Email = input.Email,
            FirstName = input.FirstName,
            LastName = input.LastName,
            Status = AdminApprovalStatus.Pending,
            RequestedAt = DateTime.UtcNow,
        };

        request = await _repository.CreateAsync(request, cancellationToken);
        _logger.LogInformation(
            "Admin access request submitted. requestId={RequestId} clerkUserId={ClerkUserId} email={Email}",
            request.Id, request.ClerkUserId, request.Email);
        return request;
    }

    public async Task<IReadOnlyList<AdminApprovalRequest>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.ListByStatusAsync(AdminApprovalStatus.Pending, cancellationToken);
    }

    public async Task<AdminApprovalRequest> ApproveAsync(
        Guid requestId,
        string reviewerClerkUserId,
        CancellationToken cancellationToken = default)
    {
        var request = await RequirePendingAsync(requestId, cancellationToken);

        // Grant first: if the Clerk Backend API call fails the request stays Pending.
        await _clerkAdminClient.GrantAdminRoleAsync(request.ClerkUserId, cancellationToken);

        request.Status = AdminApprovalStatus.Approved;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByClerkUserId = reviewerClerkUserId;
        await _repository.UpdateAsync(request, cancellationToken);

        await PromoteLocalUserAsync(request.ClerkUserId, cancellationToken);

        _logger.LogInformation(
            "Admin access request approved. requestId={RequestId} reviewer={Reviewer}",
            request.Id, reviewerClerkUserId);
        return request;
    }

    public async Task<AdminApprovalRequest> RejectAsync(
        Guid requestId,
        string reviewerClerkUserId,
        CancellationToken cancellationToken = default)
    {
        var request = await RequirePendingAsync(requestId, cancellationToken);

        request.Status = AdminApprovalStatus.Rejected;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByClerkUserId = reviewerClerkUserId;
        await _repository.UpdateAsync(request, cancellationToken);

        _logger.LogInformation(
            "Admin access request rejected. requestId={RequestId} reviewer={Reviewer}",
            request.Id, reviewerClerkUserId);
        return request;
    }

    private async Task<AdminApprovalRequest> RequirePendingAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var request = await _repository.GetByIdAsync(requestId, cancellationToken)
            ?? throw new KeyNotFoundException($"Admin approval request '{requestId}' was not found.");

        if (request.Status != AdminApprovalStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Admin approval request '{requestId}' is already {request.Status.ToString().ToLowerInvariant()}.");
        }

        return request;
    }

    private async Task PromoteLocalUserAsync(string clerkUserId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByClerkIdAsync(clerkUserId, cancellationToken);
        if (user is null)
        {
            return;
        }

        user.UserRole = Roles.Admin;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);
        await _cacheService.InvalidateAsync(user.ClerkId, cancellationToken);
    }
}
