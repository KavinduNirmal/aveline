using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Shared.DTOs;

/// <summary>Partial profile update (FR-3.2). Null fields are left unchanged.</summary>
public sealed record UpdateUserProfileRequest(
    string? FirstName = null,
    string? LastName = null,
    string? DisplayName = null,
    string? PhoneNumber = null,
    string? ProfileImageUrl = null,
    ContactPreferences? ContactPreference = null,
    bool? PushNotificationsEnabled = null);

/// <summary>A page of users returned by the cross-organization admin search (FR-3.7).</summary>
public sealed record PagedUsers(
    IReadOnlyList<UserDto> Items,
    int Page,
    int PageSize,
    int Total);
