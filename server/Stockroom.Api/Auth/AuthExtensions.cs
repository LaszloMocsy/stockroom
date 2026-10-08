using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Stockroom.Data.Auth;

namespace Stockroom.Api.Auth;

internal static class AuthExtensions
{
    /// <summary>Registers bearer authentication with the access tokens from <see cref="AccessTokenIssuer"/>.</summary>
    public static IServiceCollection AddStockroomAuth(this IServiceCollection services)
    {
        services.AddSingleton<AccessTokenKey>();
        services.AddSingleton<AccessTokenIssuer>();
        services.AddScoped<RefreshTokenService>();
        services.AddScoped<SigningKeyStore>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<AccessTokenKey>((options, key) =>
            {
                // Keep claim names as issued ("sub", "role") instead of mapping them to long URIs.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = AccessTokenIssuer.Issuer,
                    ValidAudience = AccessTokenIssuer.Audience,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    IssuerSigningKeyResolver = (_, _, _, _) => [key.Value],
                    NameClaimType = JwtRegisteredClaimNames.Name,
                    RoleClaimType = AccessTokenIssuer.RoleClaim,

                    // The same server issues and checks tokens, so there is no clock drift to allow for.
                    ClockSkew = TimeSpan.Zero,
                };
            });
        services.AddAuthorization();
        return services;
    }

    /// <summary>Loads the signing key from the database, creating it on first start. Call it after migrating.</summary>
    public static async Task LoadAccessTokenKeyAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<SigningKeyStore>();
        app.Services.GetRequiredService<AccessTokenKey>().Load(await store.GetOrCreateAsync(app.Lifetime.ApplicationStopping));
    }
}
