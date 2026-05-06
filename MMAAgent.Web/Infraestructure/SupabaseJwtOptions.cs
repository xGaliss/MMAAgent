namespace MMAAgent.Web.Infrastructure;

public sealed class SupabaseJwtOptions
{
    public const string SectionName = "SupabaseAuth";

    public bool Enabled { get; set; } = false;
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string[] Audiences { get; set; } = [];
    public bool RequireHttpsMetadata { get; set; } = true;
    public bool ValidateIssuer { get; set; } = true;
    public bool ValidateAudience { get; set; } = true;
    public bool ValidateLifetime { get; set; } = true;
    public string ProviderName { get; set; } = "supabase";
}
