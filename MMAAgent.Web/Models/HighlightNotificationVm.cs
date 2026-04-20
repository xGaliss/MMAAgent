namespace MMAAgent.Web.Models;

public sealed record HighlightNotificationVm(
    int Id,
    string MessageType,
    string Subject,
    string Body,
    string CreatedDate,
    string Tone);
