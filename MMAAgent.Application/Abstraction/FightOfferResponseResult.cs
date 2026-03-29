namespace MMAAgent.Application.Abstractions;

public sealed class FightOfferResponseResult
{
    public bool Success { get; }
    public string Message { get; }
    public int OfferId { get; }
    public int? EventId { get; }
    public int? FightId { get; }

    public FightOfferResponseResult(bool success, string message, int offerId, int? eventId, int? fightId)
    {
        Success = success;
        Message = message;
        OfferId = offerId;
        EventId = eventId;
        FightId = fightId;
    }

    public static FightOfferResponseResult Ok(string message, int offerId, int? eventId = null, int? fightId = null)
        => new(true, message, offerId, eventId, fightId);

    public static FightOfferResponseResult Fail(string message, int offerId)
        => new(false, message, offerId, null, null);
}