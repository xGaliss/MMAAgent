namespace MMAAgent.Desktop.ViewModels
{
    public sealed record EventListItem(int Id, string EventDate, string Name);
    public sealed record FightListItem(string WeightClass, string Matchup, string Winner, string Method, bool IsTitle);
}