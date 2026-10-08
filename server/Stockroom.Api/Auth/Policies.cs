using Microsoft.AspNetCore.Authorization;
using Stockroom.Core.Users;

namespace Stockroom.Api.Auth;

/// <summary>
/// Authorisation policies for the two roles (spec 2.1). <see cref="RequireStaff"/> is also the default
/// policy, which Program.cs applies to every endpoint: only endpoints marked <c>AllowAnonymous</c> are
/// public, and only ADMIN endpoints need to say more.
/// </summary>
/// <remarks>
/// It is applied through a route group rather than as the fallback policy, because the fallback also
/// covers requests that match no endpoint and would turn every 404 and 405 into a 401.
/// </remarks>
internal static class Policies
{
    /// <summary>Any logged-in user. ADMIN can do everything STAFF can.</summary>
    public const string RequireStaff = "RequireStaff";

    /// <summary>Logged-in ADMIN users only.</summary>
    public const string RequireAdmin = "RequireAdmin";

    public static AuthorizationPolicy Staff { get; } = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(Roles.Staff, Roles.Admin)
        .Build();

    public static AuthorizationPolicy Admin { get; } = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(Roles.Admin)
        .Build();
}

internal static class PolicyEndpointExtensions
{
    /// <summary>Restricts the endpoints to logged-in users with either role.</summary>
    public static TBuilder RequireStaff<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(Policies.RequireStaff);

    /// <summary>Restricts the endpoints to ADMIN users.</summary>
    public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(Policies.RequireAdmin);
}
