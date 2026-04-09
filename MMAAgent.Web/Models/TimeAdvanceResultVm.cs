namespace MMAAgent.Web.Models;

public sealed record TimeAdvanceResultVm(
    bool Success,
    int DaysAdvanced,
    int WeeksAdvanced,
    string? TargetDate,
    string? Headline,
    string Message);
