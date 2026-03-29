namespace MMAAgent.Web.Helpers;

public static class PromotionAssetsHelper
{
    public static string GetPromotionLogo(string? promotionName)
    {
        var key = promotionName?.Trim().ToLowerInvariant();

        if (key is null)
            return "/images/promotions/default.png";

        if (key.Contains("ufc")) return "/images/promotions/ufc.png";
        if (key.Contains("bellator")) return "/images/promotions/bellator.png";
        if (key.Contains("pfl")) return "/images/promotions/pfl.png";
        if (key.Contains("ksw")) return "/images/promotions/ksw.png";
        if (key.Contains("lux")) return "/images/promotions/lux.png";
        if (key.Contains("latam")) return "/images/promotions/regional-latam.png";

        return "/images/promotions/default.png";
    }

    private static string GetPromotionClass(string? name)
    {
        return name?.ToLower() switch
        {
            "ufc" => "ufc",
            "bellator" => "bellator",
            "pfl" => "pfl",
            "ksw" => "ksw",
            "lux fight league" => "lux",
            "regional latam" => "regional",
            _ => ""
        };
    }
}