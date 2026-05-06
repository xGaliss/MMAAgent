using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MMAAgent.Web.Infrastructure;

public static class AuthBridgeDefaults
{
    public const string Scheme = "MmaAuthBridge";
    public const string HybridScheme = "MmaHybridAuth";
}

public sealed class AuthBridgeAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AuthBridgeOptions _bridgeOptions;

    public AuthBridgeAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        IOptions<AuthBridgeOptions> bridgeOptions)
        : base(options, logger, encoder, clock)
    {
        _bridgeOptions = bridgeOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_bridgeOptions.AllowDevelopmentHeaders)
            return Task.FromResult(AuthenticateResult.NoResult());

        var userId = Request.Headers[_bridgeOptions.UserIdHeader].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(userId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var displayName = Request.Headers[_bridgeOptions.DisplayNameHeader].FirstOrDefault()?.Trim();
        var provider = Request.Headers[_bridgeOptions.ProviderHeader].FirstOrDefault()?.Trim();
        var providerUserId = Request.Headers[_bridgeOptions.ProviderUserIdHeader].FirstOrDefault()?.Trim();

        var resolvedProvider = string.IsNullOrWhiteSpace(provider)
            ? _bridgeOptions.DevelopmentHeaderProviderName
            : provider;
        var resolvedProviderUserId = string.IsNullOrWhiteSpace(providerUserId)
            ? userId
            : providerUserId;
        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? userId
            : displayName;

        var claims = new List<Claim>
        {
            new("mma_user_id", userId),
            new("mma_auth_mode", AuthModes.Header),
            new("provider", resolvedProvider),
            new("sub", resolvedProviderUserId),
            new(ClaimTypes.NameIdentifier, resolvedProviderUserId),
            new(ClaimTypes.Name, resolvedDisplayName),
            new("name", resolvedDisplayName)
        };

        var identity = new ClaimsIdentity(claims, AuthBridgeDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthBridgeDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
