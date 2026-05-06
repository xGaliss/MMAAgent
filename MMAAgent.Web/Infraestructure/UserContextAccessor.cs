using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace MMAAgent.Web.Infrastructure;

public static class AuthModes
{
    public const string Local = "local";
    public const string Header = "header";
    public const string External = "external";
}

public sealed record UserIdentityContext(
    string UserId,
    string DisplayName,
    bool IsAuthenticated,
    string AuthMode,
    string? Provider,
    string? ProviderUserId);

public interface IUserContextAccessor
{
    string CurrentUserId { get; }
    string DisplayName { get; }
    bool IsAuthenticated { get; }
    string AuthMode { get; }
    string? Provider { get; }
    string? ProviderUserId { get; }
    UserIdentityContext GetCurrent();
}

public sealed class HttpAwareUserContextAccessor : IUserContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthBridgeOptions _options;

    public HttpAwareUserContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        IOptions<AuthBridgeOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
    }

    public string CurrentUserId => GetCurrent().UserId;
    public string DisplayName => GetCurrent().DisplayName;
    public bool IsAuthenticated => GetCurrent().IsAuthenticated;
    public string AuthMode => GetCurrent().AuthMode;
    public string? Provider => GetCurrent().Provider;
    public string? ProviderUserId => GetCurrent().ProviderUserId;

    public UserIdentityContext GetCurrent()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            var claimsPrincipal = httpContext.User;
            if (claimsPrincipal.Identity?.IsAuthenticated == true)
                return BuildExternalIdentity(claimsPrincipal);

            var headerIdentity = BuildHeaderIdentity(httpContext.Request.Headers);
            if (headerIdentity is not null)
                return headerIdentity;
        }

        return BuildLocalIdentity();
    }

    private UserIdentityContext BuildExternalIdentity(ClaimsPrincipal principal)
    {
        var providerUserId =
            GetClaimValue(principal, "sub")
            ?? GetClaimValue(principal, ClaimTypes.NameIdentifier)
            ?? principal.Identity?.Name
            ?? "unknown";

        var logicalUserId =
            GetClaimValue(principal, "mma_user_id")
            ?? $"auth:{providerUserId.Trim().ToLowerInvariant()}";

        var displayName =
            GetClaimValue(principal, "name")
            ?? GetClaimValue(principal, ClaimTypes.Name)
            ?? GetClaimValue(principal, "preferred_username")
            ?? GetClaimValue(principal, ClaimTypes.Email)
            ?? providerUserId;

        var provider =
            GetClaimValue(principal, "iss")
            ?? GetClaimValue(principal, "provider")
            ?? "external";

        var normalizedProvider = NormalizeProvider(provider);
        var authMode =
            GetClaimValue(principal, "mma_auth_mode")
            ?? AuthModes.External;

        return new UserIdentityContext(
            UserId: logicalUserId.Trim(),
            DisplayName: displayName.Trim(),
            IsAuthenticated: true,
            AuthMode: authMode,
            Provider: normalizedProvider,
            ProviderUserId: providerUserId.Trim());
    }

    private UserIdentityContext? BuildHeaderIdentity(IHeaderDictionary headers)
    {
        if (!_options.AllowDevelopmentHeaders)
            return null;

        var userId = headers[_options.UserIdHeader].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var displayName = headers[_options.DisplayNameHeader].FirstOrDefault()?.Trim();
        var provider = headers[_options.ProviderHeader].FirstOrDefault()?.Trim();
        var providerUserId = headers[_options.ProviderUserIdHeader].FirstOrDefault()?.Trim();

        return new UserIdentityContext(
            UserId: userId,
            DisplayName: string.IsNullOrWhiteSpace(displayName) ? userId : displayName,
            IsAuthenticated: true,
            AuthMode: AuthModes.Header,
            Provider: string.IsNullOrWhiteSpace(provider) ? _options.DevelopmentHeaderProviderName : NormalizeProvider(provider),
            ProviderUserId: string.IsNullOrWhiteSpace(providerUserId) ? userId : providerUserId);
    }

    private UserIdentityContext BuildLocalIdentity()
    {
        var rawUserName = string.IsNullOrWhiteSpace(Environment.UserName) ? "player" : Environment.UserName.Trim();
        return new UserIdentityContext(
            UserId: $"local:{rawUserName.ToLowerInvariant()}",
            DisplayName: rawUserName,
            IsAuthenticated: false,
            AuthMode: AuthModes.Local,
            Provider: _options.LocalProviderName,
            ProviderUserId: rawUserName.ToLowerInvariant());
    }

    private static string? GetClaimValue(ClaimsPrincipal principal, string claimType)
        => principal.FindFirst(claimType)?.Value;

    private string NormalizeProvider(string provider)
    {
        var trimmed = provider.Trim();
        if (_options.SupabaseIssuerHints.Any(x =>
                !string.IsNullOrWhiteSpace(x) &&
                trimmed.Contains(x, StringComparison.OrdinalIgnoreCase)))
        {
            return "supabase";
        }

        return trimmed;
    }
}
