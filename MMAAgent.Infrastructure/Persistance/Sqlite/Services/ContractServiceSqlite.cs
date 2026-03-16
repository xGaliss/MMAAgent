using Microsoft.Data.Sqlite;



namespace MMAAgent.Infrastructure.Persistence.Sqlite.Services
{
    public sealed class ContractServiceSqlite : IContractServiceSqlite
    {
        public bool AiAutoRenewEnabled { get; set; } = true;

        public async Task PostFightContractTickAsync(SqliteConnection conn, SqliteTransaction tx, int fighterId)
        {
            if (!AiAutoRenewEnabled) return;

            // todo usando conn+tx
            // 1) leer fighter lite
            var f = await GetFighterLiteAsync(conn, tx, fighterId);
            if (f is null) return;
            if (f.Retired != 0) return;
            if (f.PromotionId <= 0) return;
            if (!string.Equals(f.ContractStatus, "Active", StringComparison.OrdinalIgnoreCase)) return;

            int remaining = f.ContractFightsRemaining;
            if (remaining <= 0)
            {
                await HandleContractExpiredAsync(conn, tx, fighterId, f.PromotionId, f.WeightClass);
                return;
            }

            int newRemaining = Math.Max(0, remaining - 1);

            await ExecAsync(conn, tx,
                @"UPDATE Fighters SET ContractFightsRemaining = $r WHERE Id = $id;",
                ("$r", newRemaining), ("$id", fighterId));

            if (newRemaining == 0)
                await HandleContractExpiredAsync(conn, tx, fighterId, f.PromotionId, f.WeightClass);
        }

        // --- helpers (conn/tx) ---
        private static async Task<FighterLite?> GetFighterLiteAsync(SqliteConnection conn, SqliteTransaction tx, int fighterId)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT Id, COALESCE(PromotionId,0) AS PromotionId, WeightClass, Retired,
       ContractStatus, ContractFightsRemaining
FROM Fighters WHERE Id = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", fighterId);

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            return new FighterLite
            {
                Id = Convert.ToInt32(r["Id"]),
                PromotionId = Convert.ToInt32(r["PromotionId"]),
                WeightClass = r["WeightClass"]?.ToString() ?? "",
                Retired = Convert.ToInt32(r["Retired"]),
                ContractStatus = r["ContractStatus"]?.ToString() ?? "",
                ContractFightsRemaining = Convert.ToInt32(r["ContractFightsRemaining"]),
            };
        }

        private static Task ExecAsync(SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] p)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v);
            return cmd.ExecuteNonQueryAsync();
        }

        private Task HandleContractExpiredAsync(SqliteConnection conn, SqliteTransaction tx, int fighterId, int promotionId, string weightClass)
        {
            // aquí metes tu lógica de renew/offer/release usando conn+tx
            return Task.CompletedTask;
        }

        private sealed class FighterLite
        {
            public int Id { get; set; }
            public int PromotionId { get; set; }
            public string WeightClass { get; set; } = "";
            public int Retired { get; set; }
            public string ContractStatus { get; set; } = "";
            public int ContractFightsRemaining { get; set; }
        }
    }
}