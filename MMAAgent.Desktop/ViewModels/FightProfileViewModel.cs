using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using System.Collections.ObjectModel;

namespace MMAAgent.Desktop.ViewModels;

public sealed class FightProfileViewModel : ObservableObject
{
    private readonly SqliteConnectionFactory _factory;
    private readonly ISavePathProvider _savePath;

    public ObservableCollection<FightHistoryItem> History { get; } = new();

    private FighterProfile? _fighter;
    public FighterProfile? Fighter
    {
        get => _fighter;
        private set => SetProperty(ref _fighter, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public FightProfileViewModel(SqliteConnectionFactory factory, ISavePathProvider savePath)
    {
        _factory = factory;
        _savePath = savePath;
    }

    public async Task LoadAsync(int fighterId, int take = 15)
    {
        if (string.IsNullOrWhiteSpace(_savePath.CurrentPath)) return;

        IsBusy = true;
        try
        {
            using var conn = _factory.CreateConnection();
            using var tx = conn.BeginTransaction();

            Fighter = await LoadProfileAsync(conn, tx, fighterId);
            var fights = await LoadHistoryAsync(conn, tx, fighterId, take);

            History.Clear();
            foreach (var it in fights) History.Add(it);

            tx.Commit();
        }
        finally { IsBusy = false; }
    }

    private static async Task<FighterProfile?> LoadProfileAsync(SqliteConnection conn, SqliteTransaction tx, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT 
  f.Id,
  f.FirstName || ' ' || f.LastName AS Name,
  COALESCE(c.Name,'') AS CountryName,
  f.WeightClass, f.Age,
  f.Wins, f.Losses, f.Draws,
  f.Skill, f.Potential, f.Popularity,
  f.Striking, f.Grappling, f.Wrestling, f.Cardio, f.Chin, f.FightIQ,
  f.ContractStatus,
  f.PromotionId,
  COALESCE(p.Name,'') AS PromotionName,
  f.Salary, f.ContractFightsRemaining, f.TotalFightsInContract,
  COALESCE(pr.RankPosition, 0) AS RankPosition,
  CASE WHEN t.ChampionFighterId = f.Id THEN 1 ELSE 0 END AS IsChampion
FROM Fighters f
LEFT JOIN Countries c ON c.Id = f.CountryId
LEFT JOIN Promotions p ON p.Id = f.PromotionId
LEFT JOIN PromotionRankings pr ON pr.FighterId = f.Id AND pr.PromotionId = f.PromotionId AND pr.WeightClass = f.WeightClass
LEFT JOIN Titles t ON t.PromotionId = f.PromotionId AND t.WeightClass = f.WeightClass
WHERE f.Id = $id
LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        int rank = Convert.ToInt32(r["RankPosition"]);
        return new FighterProfile(
            Id: Convert.ToInt32(r["Id"]),
            Name: r["Name"]?.ToString() ?? "",
            CountryName: r["CountryName"]?.ToString() ?? "",
            WeightClass: r["WeightClass"]?.ToString() ?? "",
            Age: Convert.ToInt32(r["Age"]),
            Wins: Convert.ToInt32(r["Wins"]),
            Losses: Convert.ToInt32(r["Losses"]),
            Draws: Convert.ToInt32(r["Draws"]),
            Skill: Convert.ToInt32(r["Skill"]),
            Potential: Convert.ToInt32(r["Potential"]),
            Popularity: Convert.ToInt32(r["Popularity"]),
            Striking: Convert.ToInt32(r["Striking"]),
            Grappling: Convert.ToInt32(r["Grappling"]),
            Wrestling: Convert.ToInt32(r["Wrestling"]),
            Cardio: Convert.ToInt32(r["Cardio"]),
            Chin: Convert.ToInt32(r["Chin"]),
            FightIQ: Convert.ToInt32(r["FightIQ"]),
            ContractStatus: r["ContractStatus"]?.ToString() ?? "",
            PromotionId: r["PromotionId"] == DBNull.Value ? null : Convert.ToInt32(r["PromotionId"]),
            PromotionName: r["PromotionName"]?.ToString(),
            Salary: Convert.ToInt32(r["Salary"]),
            ContractFightsRemaining: Convert.ToInt32(r["ContractFightsRemaining"]),
            TotalFightsInContract: Convert.ToInt32(r["TotalFightsInContract"]),
            RankPosition: rank > 0 ? rank : null,
            IsChampion: Convert.ToInt32(r["IsChampion"]) == 1
        );
    }

    private static async Task<List<FightHistoryItem>> LoadHistoryAsync(SqliteConnection conn, SqliteTransaction tx, int id, int take)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
  fh.FightDate,
  CASE WHEN fh.WinnerId = $id THEN 1 ELSE 0 END AS Won,
  fh.Method,
  fh.IsTitle,
  p.Name AS PromotionName,
  e.Name AS EventName,
  -- opponent name
  (op.FirstName || ' ' || op.LastName) AS Opponent
FROM FightHistory fh
JOIN Promotions p ON p.Id = fh.PromotionId
LEFT JOIN Events e ON e.Id = fh.EventId
JOIN Fighters op ON op.Id = CASE WHEN fh.FighterAId = $id THEN fh.FighterBId ELSE fh.FighterAId END
WHERE (fh.FighterAId = $id OR fh.FighterBId = $id)
ORDER BY fh.Id DESC
LIMIT $take;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$take", take);

        var list = new List<FightHistoryItem>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            bool won = Convert.ToInt32(r["Won"]) == 1;
            list.Add(new FightHistoryItem(
                Date: r["FightDate"]?.ToString() ?? "",
                Opponent: r["Opponent"]?.ToString() ?? "",
                Result: won ? "W" : "L",
                Method: r["Method"]?.ToString() ?? "",
                IsTitle: Convert.ToInt32(r["IsTitle"]) == 1,
                Promotion: r["PromotionName"]?.ToString() ?? "",
                EventName: r["EventName"]?.ToString()
            ));
        }
        return list;
    }
}