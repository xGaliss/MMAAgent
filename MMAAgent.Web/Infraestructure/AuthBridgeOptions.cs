namespace MMAAgent.Web.Infrastructure;

public sealed class AuthBridgeOptions
{
    public const string SectionName = "AuthBridge";

    public bool AllowDevelopmentHeaders { get; set; } = true;
    public string UserIdHeader { get; set; } = "X-MMA-User-Id";
    public string DisplayNameHeader { get; set; } = "X-MMA-Display-Name";
    public string ProviderHeader { get; set; } = "X-MMA-Auth-Provider";
    public string ProviderUserIdHeader { get; set; } = "X-MMA-Provider-User-Id";
    public string LocalProviderName { get; set; } = "local-dev";
    public string DevelopmentHeaderProviderName { get; set; } = "header-dev";
    public string[] SupabaseIssuerHints { get; set; } =
    [
        "supabase.co/auth/v1",
        "supabase"
    ];
}
