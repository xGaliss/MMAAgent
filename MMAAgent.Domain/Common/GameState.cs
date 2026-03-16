namespace MMAAgent.Domain.Common
{
    public sealed class GameState
    {
        public int Id { get; set; } = 1;

        // en DB son TEXT, mantenlo string "yyyy-MM-dd"
        public string StartDate { get; set; } = "";
        public string CurrentDate { get; set; } = "";

        public int CurrentWeek { get; set; } = 1;
        public int CurrentYear { get; set; } = 1;

        public int WorldSeed { get; set; } = 0;
    }
}