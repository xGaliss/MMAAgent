namespace MMAAgent.Web.Helpers;

public static class CountryFlagHelper
{
    public static string? GetFlagCode(string? countryName)
    {
        if (string.IsNullOrWhiteSpace(countryName))
            return null;

        return countryName.Trim() switch
        {
            "USA" or "United States" or "United States of America" => "us",
            "Brazil" or "Brasil" => "br",
            "Russia" => "ru",
            "Mexico" or "México" => "mx",
            "Spain" or "España" => "es",
            "Argentina" => "ar",
            "Japan" => "jp",
            "France" => "fr",
            "UK" or "United Kingdom" or "England" or "Great Britain" => "gb",
            "Canada" => "ca",
            "Colombia" => "co",
            "Chile" => "cl",
            "Peru" or "Perú" => "pe",
            "Netherlands" => "nl",
            "Poland" => "pl",
            "Sweden" => "se",
            "Nigeria" => "ng",
            "Australia" => "au",
            "South Korea" or "Korea" or "Republic of Korea" => "kr",
            "China" or "PRC" or "People's Republic of China" => "cn",
            _ => null
        };
    }

    public static string? GetFlagImageUrl(string? countryName)
    {
        var code = GetFlagCode(countryName);
        return string.IsNullOrWhiteSpace(code)
            ? null
            : $"https://flagcdn.com/{code}.svg";
    }
}
