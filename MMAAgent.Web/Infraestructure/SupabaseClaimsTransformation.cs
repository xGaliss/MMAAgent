using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MMAAgent.Web.Infrastructure;

public sealed class SupabaseClaimsTransformation : IClaimsTransformation
{
    private readonly AuthBridgeOptions _authBridgeOptions;
    private readonly SupabaseJwtOptions _supabaseOptions;

    public SupabaseClaimsTransformation(
        IOptions<AuthBridgeOptions> authBridgeOptions,
        IOptions<SupabaseJwtOptions> supabaseOptions)
    {
        _authBridgeOptions = authBridgeOptions.Value;
        _supabaseOptions = supabaseOptions.Value;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            return Task.FromResult(principal);

        if (identity.HasClaim(x => x.Type == "mma_user_id"))
            return Task.FromResult(principal);

        var providerUserId =
            principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.Identity?.Name;

        if (string.IsNullOrWhiteSpace(providerUserId))
            return Task.FromResult(principal);

        var provider =
            principal.FindFirst("iss")?.Value
            ?? principal.FindFirst("provider")?.Value
            ?? _supabaseOptions.ProviderName;

        var normalizedProvider = NormalizeProvider(provider);
        var displayName =
            principal.FindFirst("name")?.Value
            ?? principal.FindFirst(ClaimTypes.Name)?.Value
            ?? principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value
            ?? providerUserId;

        identity.AddClaim(new Claim("mma_user_id", $"auth:{providerUserId.Trim().ToLowerInvariant()}"));
        identity.AddClaim(new Claim("mma_auth_mode", AuthModes.External));

        if (!identity.HasClaim(x => x.Type == "provider"))
            identity.AddClaim(new Claim("provider", normalizedProvider));

        if (!identity.HasClaim(x => x.Type == "name") && !string.IsNullOrWhiteSpace(displayName))
            identity.AddClaim(new Claim("name", displayName.Trim()));

        return Task.FromResult(principal);
    }

    private string NormalizeProvider(string? provider)
    {
        var trimmed = string.IsNullOrWhiteSpace(provider) ? _supabaseOptions.ProviderName : provider.Trim();
        if (_authBridgeOptions.SupabaseIssuerHints.Any(x =>
                !string.IsNullOrWhiteSpace(x) &&
                trimmed.Contains(x, StringComparison.OrdinalIgnoreCase)))
        {
            return _supabaseOptions.ProviderName;
        }

        return trimmed;
    }
}
