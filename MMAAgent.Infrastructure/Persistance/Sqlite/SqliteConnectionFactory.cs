using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using System;
using System.IO;

namespace MMAAgent.Infrastructure.Persistence.Sqlite
{
    public sealed class SqliteConnectionFactory
    {
        private readonly ISavePathProvider _savePath;

        public SqliteConnectionFactory(ISavePathProvider savePath)
        {
            _savePath = savePath ?? throw new ArgumentNullException(nameof(savePath));
        }

        public SqliteConnection CreateConnection()
        {
            var path = _savePath.CurrentPath;
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("No hay partida cargada (SavePathProvider.CurrentPath es null/vacío).");

            if (!File.Exists(path))
                throw new FileNotFoundException($"No se encontró la DB de partida en: {path}", path);

            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,   // save DB debe ser RW
                Cache = SqliteCacheMode.Shared
            }.ToString();

            var conn = new SqliteConnection(cs);
            conn.Open();

            // PRAGMAs por conexión (recomendado)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
PRAGMA busy_timeout=5000;";
                cmd.ExecuteNonQuery();
            }

            return conn;
        }
    }
}