namespace MMAAgent.Web.Infrastructure;

public interface IUserContextAccessor
{
    string CurrentUserId { get; }
    string DisplayName { get; }
}

public sealed class LocalUserContextAccessor : IUserContextAccessor
{
    public string CurrentUserId { get; } =
        $"local:{(Environment.UserName ?? "player").Trim().ToLowerInvariant()}";

    public string DisplayName { get; } =
        string.IsNullOrWhiteSpace(Environment.UserName) ? "Local Player" : Environment.UserName.Trim();
}
