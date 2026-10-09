using Microsoft.IdentityModel.Tokens;

namespace Stockroom.Api.Auth;

/// <summary>
/// The key that signs and validates access tokens. It lives in the database, so it is loaded once at
/// startup (<see cref="AuthExtensions.LoadAccessTokenKeyAsync"/>) and held here for the process lifetime.
/// </summary>
internal sealed class AccessTokenKey
{
    private SymmetricSecurityKey? _key;

    public SymmetricSecurityKey Value =>
        _key ?? throw new InvalidOperationException("The access-token signing key has not been loaded; call LoadAccessTokenKeyAsync at startup.");

    public void Load(byte[] key) => _key = new SymmetricSecurityKey(key);
}
