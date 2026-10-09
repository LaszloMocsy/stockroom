using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Stockroom.Core.Users;

namespace Stockroom.Api.Auth;

/// <summary>
/// Issues the short-lived access tokens that clients send as <c>Authorization: Bearer</c> (spec 10.2):
/// JWTs signed with HMAC-SHA256. Clients can read them, so they identify the user by public ID only.
/// </summary>
internal sealed class AccessTokenIssuer(AccessTokenKey key, TimeProvider time)
{
    public const string Issuer = "stockroom";
    public const string Audience = "stockroom-api";

    /// <summary>Claim that carries the user's role, <c>ADMIN</c> or <c>STAFF</c>.</summary>
    public const string RoleClaim = "role";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly JsonWebTokenHandler _handler = new();

    public string Issue(User user, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.PublicId.ToString()),
            new(JwtRegisteredClaimNames.Name, user.UserName!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(role => new Claim(RoleClaim, role)));

        var now = time.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = now + Lifetime,
            SigningCredentials = new SigningCredentials(key.Value, SecurityAlgorithms.HmacSha256),
        });
    }
}
