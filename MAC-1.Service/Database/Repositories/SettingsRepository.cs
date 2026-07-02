using System;
using MAC_1.Service.Database.Models;
using Microsoft.Data.Sqlite;

namespace MAC_1.Service.Database.Repositories
{
    public class SettingsRepository
    {
        private readonly string _connectionString;

        public SettingsRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void Set(string key, string value)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                Set(key, value, connection);
            }
            catch (Exception) { }
        }

        public void Set(string key, string value, SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Settings (Key, Value, UpdatedAt)
                VALUES ($key, $value, datetime('now'))";

            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$value", value);
            cmd.ExecuteNonQuery();
        }

        public string? Get(string key)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
                cmd.Parameters.AddWithValue("$key", key);

                var result = cmd.ExecuteScalar();
                return result?.ToString();
            }
            catch (Exception) { return null; }
        }

        public string Get(string key, string defaultValue)
        {
            return Get(key) ?? defaultValue;
        }

        public int GetInt(string key, int defaultValue)
        {
            var val = Get(key);
            if (val != null && int.TryParse(val, out int result))
                return result;
            return defaultValue;
        }

        public bool GetBool(string key, bool defaultValue)
        {
            var val = Get(key);
            if (val != null && bool.TryParse(val, out bool result))
                return result;
            return defaultValue;
        }

        public void Delete(string key)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM Settings WHERE Key = $key";
                cmd.Parameters.AddWithValue("$key", key);
                cmd.ExecuteNonQuery();
            }
            catch (Exception) { }
        }
    }
}
