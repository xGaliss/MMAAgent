namespace MMAAgent.Web.Infrastructure;

public sealed class ApiSecurityOptions
{
    public const string SectionName = "ApiSecurity";

    public bool RequireExternalAuthForSaveOperations { get; set; } = true;
    public bool AllowDevelopmentBypassForSaveOperations { get; set; }
}
