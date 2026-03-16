using Microsoft.Data.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Services
{
    public interface IContractServiceSqlite
    {
        Task PostFightContractTickAsync(SqliteConnection conn, SqliteTransaction tx, int fighterId);
    }
}