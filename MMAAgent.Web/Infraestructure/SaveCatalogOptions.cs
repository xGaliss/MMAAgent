namespace MMAAgent.Web.Infrastructure;

public static class SaveCatalogProviders
{
    public const string LocalJson = "local-json";
    public const string SupabasePostgres = "supabase-postgres";
}

public sealed class SaveCatalogOptions
{
    public const string SectionName = "SaveCatalog";

    public string Provider { get; set; } = SaveCatalogProviders.LocalJson;
    public bool MirrorLocalJson { get; set; } = true;
    public string Schema { get; set; } = "mma_agent";
    public string? PostgresConnectionString { get; set; }
    public string? FallbackPostgresConnectionString { get; set; }
    public string? BackendInstanceName { get; set; } = "supabase";
}
