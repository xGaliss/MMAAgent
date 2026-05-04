namespace MMAAgent.Web.Models;

public sealed record WorldFeedItemVm(
    string Bucket,
    string Headline,
    string Summary,
    string Date,
    string Tone,
    string? LinkHref);

public sealed class WorldFeedVm
{
    public IReadOnlyList<WorldFeedItemVm> Headlines { get; init; } = Array.Empty<WorldFeedItemVm>();
    public IReadOnlyList<WorldFeedItemVm> Storylines { get; init; } = Array.Empty<WorldFeedItemVm>();
}
