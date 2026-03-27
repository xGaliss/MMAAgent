namespace MMAAgent.Web.Models;

public sealed record InboxMessageVm(
    int Id,
    string MessageType,
    string Subject,
    string Body,
    string CreatedDate,
    bool IsRead);

public sealed record FightOfferVm(
    int OfferId,
    int FighterId,
    string FighterName,
    int OpponentFighterId,
    string OpponentName,
    int Purse,
    int WinBonus,
    int WeeksUntilFight,
    bool IsTitleFight,
    string Status);
