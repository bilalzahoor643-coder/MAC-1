using System;
using System.Collections.Generic;
using MAC_1.Service.Database.Models;
using Microsoft.Data.Sqlite;

namespace MAC_1.Service.Database.Repositories
{
    public class HistoryRepository
    {
        private readonly string _connectionString;

        public HistoryRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void Save(HistoryEntity history)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                Save(history, connection);
            }
            catch (Exception) { }
        }

        public void Save(HistoryEntity history, SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO History (
                    SessionId, StartTime, EndTime, Result, FinalPath, FinalSize, ErrorMessage
                ) VALUES (
                    $sid, $start, $end, $result, $path, $size, $error
                )";

            cmd.Parameters.AddWithValue("$sid", history.SessionId);
            cmd.Parameters.AddWithValue("$start", history.StartTime.ToString("o"));
            cmd.Parameters.AddWithValue("$end", history.EndTime?.ToString("o") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$result", history.Result);
            cmd.Parameters.AddWithValue("$path", history.FinalPath);
            cmd.Parameters.AddWithValue("$size", history.FinalSize);
            cmd.Parameters.AddWithValue("$error", (object?)history.ErrorMessage ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public List<HistoryEntity> GetAll()
        {
            var list = new List<HistoryEntity>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM History ORDER BY StartTime DESC";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new HistoryEntity
                    {
                        SessionId = reader.GetString(reader.GetOrdinal("SessionId")),
                        StartTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("StartTime"))),
                        EndTime = reader.IsDBNull(reader.GetOrdinal("EndTime")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("EndTime"))),
                        Result = reader.GetString(reader.GetOrdinal("Result")),
                        FinalPath = reader.GetString(reader.GetOrdinal("FinalPath")),
                        FinalSize = reader.GetInt64(reader.GetOrdinal("FinalSize")),
                        ErrorMessage = reader.IsDBNull(reader.GetOrdinal("ErrorMessage")) ? null : reader.GetString(reader.GetOrdinal("ErrorMessage"))
                    });
                }
            }
            catch (Exception) { }
            return list;
        }

        public void Delete(string sessionId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM History WHERE SessionId = $sid";
                cmd.Parameters.AddWithValue("$sid", sessionId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception) { }
        }
    }
}
