using System;
using System.Collections.Generic;
using System.Text.Json;
using MAC_1.Service.Database.Models;
using Microsoft.Data.Sqlite;

namespace MAC_1.Service.Database.Repositories
{
    public class DownloadSessionRepository
    {
        private readonly string _connectionString;

        public DownloadSessionRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void Save(DownloadSessionEntity session)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                Save(session, connection);
            }
            catch (Exception) { }
        }

        public void Save(DownloadSessionEntity session, SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO DownloadSessions (
                    SessionId, Url, FinalUrl, Filename, FileExtension, FileSize,
                    MimeType, Referrer, Origin, RequestMethod, UserAgent, Host,
                    Status, SavePath, Category, BytesDownloaded, Connections,
                    ResumeSupported, ETag, LastModified, AcceptRanges,
                    RawHeadersJson, RawCookiesJson, RawClientHintsJson,
                    RawTabJson, RawRedirectChainJson, RawBrowserHeadersJson,
                    RawPostDataJson, CreatedAt, UpdatedAt
                ) VALUES (
                    $sid, $url, $finalUrl, $filename, $ext, $fileSize,
                    $mime, $referrer, $origin, $method, $ua, $host,
                    $status, $savePath, $category, $bytes, $conn,
                    $resume, $etag, $lastMod, $accept,
                    $headers, $cookies, $hints,
                    $tab, $redirect, $browserHeaders, $postData, $created, $updated
                )";

            cmd.Parameters.AddWithValue("$sid", session.SessionId);
            cmd.Parameters.AddWithValue("$url", session.Url);
            cmd.Parameters.AddWithValue("$finalUrl", session.FinalUrl);
            cmd.Parameters.AddWithValue("$filename", session.Filename);
            cmd.Parameters.AddWithValue("$ext", session.FileExtension);
            cmd.Parameters.AddWithValue("$fileSize", session.FileSize);
            cmd.Parameters.AddWithValue("$mime", session.MimeType);
            cmd.Parameters.AddWithValue("$referrer", session.Referrer);
            cmd.Parameters.AddWithValue("$origin", session.Origin);
            cmd.Parameters.AddWithValue("$method", session.RequestMethod);
            cmd.Parameters.AddWithValue("$ua", session.UserAgent);
            cmd.Parameters.AddWithValue("$host", session.Host);
            cmd.Parameters.AddWithValue("$status", session.Status);
            cmd.Parameters.AddWithValue("$savePath", session.SavePath);
            cmd.Parameters.AddWithValue("$category", session.Category);
            cmd.Parameters.AddWithValue("$bytes", session.BytesDownloaded);
            cmd.Parameters.AddWithValue("$conn", session.Connections);
            cmd.Parameters.AddWithValue("$resume", session.ResumeSupported ? 1 : 0);
            cmd.Parameters.AddWithValue("$etag", (object?)session.ETag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lastMod", (object?)session.LastModified ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$accept", (object?)session.AcceptRanges ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$headers", session.RawHeadersJson);
            cmd.Parameters.AddWithValue("$cookies", session.RawCookiesJson);
            cmd.Parameters.AddWithValue("$hints", session.RawClientHintsJson);
            cmd.Parameters.AddWithValue("$tab", session.RawTabJson);
            cmd.Parameters.AddWithValue("$redirect", session.RawRedirectChainJson);
            cmd.Parameters.AddWithValue("$browserHeaders", session.RawBrowserHeadersJson);
            cmd.Parameters.AddWithValue("$postData", session.RawPostDataJson);
            cmd.Parameters.AddWithValue("$created", session.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$updated", session.UpdatedAt.ToString("o"));

            cmd.ExecuteNonQuery();
        }

        public void UpdateStatus(string sessionId, string status)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                UpdateStatus(sessionId, status, connection);
            }
            catch (Exception) { }
        }

        public void UpdateStatus(string sessionId, string status, SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE DownloadSessions 
                SET Status = $status, UpdatedAt = datetime('now')
                WHERE SessionId = $sid";

            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$sid", sessionId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateBytes(string sessionId, long bytesDownloaded)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                UpdateBytes(sessionId, bytesDownloaded, connection);
            }
            catch (Exception) { }
        }

        public void UpdateBytes(string sessionId, long bytesDownloaded, SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE DownloadSessions 
                SET BytesDownloaded = $bytes, UpdatedAt = datetime('now')
                WHERE SessionId = $sid";

            cmd.Parameters.AddWithValue("$bytes", bytesDownloaded);
            cmd.Parameters.AddWithValue("$sid", sessionId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateProgress(string sessionId, long bytesDownloaded, long fileSize,
            double progress, double speed, double averageSpeed, double eta,
            string savePath, SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE DownloadSessions 
                SET BytesDownloaded = $bytes, FileSize = $fileSize, Progress = $progress,
                    Speed = $speed, AverageSpeed = $avgSpeed, ETA = $eta,
                    SavePath = $savePath, Status = 'downloading', UpdatedAt = datetime('now')
                WHERE SessionId = $sid";

            cmd.Parameters.AddWithValue("$bytes", bytesDownloaded);
            cmd.Parameters.AddWithValue("$fileSize", fileSize);
            cmd.Parameters.AddWithValue("$progress", progress);
            cmd.Parameters.AddWithValue("$speed", speed);
            cmd.Parameters.AddWithValue("$avgSpeed", averageSpeed);
            cmd.Parameters.AddWithValue("$eta", eta);
            cmd.Parameters.AddWithValue("$savePath", savePath);
            cmd.Parameters.AddWithValue("$sid", sessionId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateMetadata(string sessionId, long fileSize, bool resumeSupported,
            int httpStatusCode, string? finalUrl, SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE DownloadSessions 
                SET FileSize = $fileSize, ResumeSupported = $resume,
                    HttpStatusCode = $httpStatus, FinalUrl = $finalUrl,
                    UpdatedAt = datetime('now')
                WHERE SessionId = $sid";

            cmd.Parameters.AddWithValue("$fileSize", fileSize);
            cmd.Parameters.AddWithValue("$resume", resumeSupported ? 1 : 0);
            cmd.Parameters.AddWithValue("$httpStatus", httpStatusCode);
            cmd.Parameters.AddWithValue("$finalUrl", finalUrl ?? string.Empty);
            cmd.Parameters.AddWithValue("$sid", sessionId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateStatusFull(string sessionId, string status, string? finalPath,
            long bytesDownloaded, long fileSize, double progress, string? errorMessage,
            SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE DownloadSessions 
                SET Status = $status, SavePath = COALESCE($savePath, SavePath),
                    BytesDownloaded = $bytes, FileSize = $fileSize, Progress = $progress,
                    ErrorMessage = $error, UpdatedAt = datetime('now')
                WHERE SessionId = $sid";

            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$savePath", (object?)finalPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bytes", bytesDownloaded);
            cmd.Parameters.AddWithValue("$fileSize", fileSize);
            cmd.Parameters.AddWithValue("$progress", progress);
            cmd.Parameters.AddWithValue("$error", (object?)errorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sid", sessionId);
            cmd.ExecuteNonQuery();
        }

        public List<DownloadSessionEntity> GetAll()
        {
            var list = new List<DownloadSessionEntity>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                return GetAll(connection);
            }
            catch (Exception) { return list; }
        }

        public List<DownloadSessionEntity> GetAll(SqliteConnection connection)
        {
            var list = new List<DownloadSessionEntity>();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM DownloadSessions ORDER BY CreatedAt DESC";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapFromReader(reader));
            }
            return list;
        }

        public List<DownloadSessionEntity> GetByStatus(string status)
        {
            var list = new List<DownloadSessionEntity>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM DownloadSessions WHERE Status = $status ORDER BY CreatedAt ASC";
                cmd.Parameters.AddWithValue("$status", status);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(MapFromReader(reader));
                }
            }
            catch (Exception) { }
            return list;
        }

        public DownloadSessionEntity? GetById(string sessionId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM DownloadSessions WHERE SessionId = $sid";
                cmd.Parameters.AddWithValue("$sid", sessionId);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                    return MapFromReader(reader);
            }
            catch (Exception) { }
            return null;
        }

        public int Count()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM DownloadSessions";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (Exception) { return 0; }
        }

        public int CountByStatus(string status)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM DownloadSessions WHERE Status = $status";
                cmd.Parameters.AddWithValue("$status", status);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (Exception) { return 0; }
        }

        public void Delete(string sessionId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM DownloadSessions WHERE SessionId = $sid";
                cmd.Parameters.AddWithValue("$sid", sessionId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception) { }
        }

        public DownloadSessionEntity? FindByUrlAndStatus(string url, string status)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM DownloadSessions WHERE Url = $url AND Status = $status ORDER BY CreatedAt DESC LIMIT 1";
                cmd.Parameters.AddWithValue("$url", url);
                cmd.Parameters.AddWithValue("$status", status);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                    return MapFromReader(reader);
            }
            catch (Exception) { }
            return null;
        }

        private DownloadSessionEntity MapFromReader(SqliteDataReader reader)
        {
            return new DownloadSessionEntity
            {
                SessionId = reader.GetString(reader.GetOrdinal("SessionId")),
                Url = reader.GetString(reader.GetOrdinal("Url")),
                FinalUrl = reader.GetString(reader.GetOrdinal("FinalUrl")),
                Filename = reader.GetString(reader.GetOrdinal("Filename")),
                FileExtension = reader.GetString(reader.GetOrdinal("FileExtension")),
                FileSize = reader.GetInt64(reader.GetOrdinal("FileSize")),
                MimeType = reader.GetString(reader.GetOrdinal("MimeType")),
                Referrer = reader.GetString(reader.GetOrdinal("Referrer")),
                Origin = reader.GetString(reader.GetOrdinal("Origin")),
                RequestMethod = reader.GetString(reader.GetOrdinal("RequestMethod")),
                UserAgent = reader.GetString(reader.GetOrdinal("UserAgent")),
                Host = reader.GetString(reader.GetOrdinal("Host")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                SavePath = reader.GetString(reader.GetOrdinal("SavePath")),
                Category = reader.GetString(reader.GetOrdinal("Category")),
                BytesDownloaded = reader.GetInt64(reader.GetOrdinal("BytesDownloaded")),
                Connections = reader.GetInt32(reader.GetOrdinal("Connections")),
                ResumeSupported = reader.GetInt32(reader.GetOrdinal("ResumeSupported")) == 1,
                ETag = reader.IsDBNull(reader.GetOrdinal("ETag")) ? null : reader.GetString(reader.GetOrdinal("ETag")),
                LastModified = reader.IsDBNull(reader.GetOrdinal("LastModified")) ? null : reader.GetString(reader.GetOrdinal("LastModified")),
                AcceptRanges = reader.IsDBNull(reader.GetOrdinal("AcceptRanges")) ? null : reader.GetString(reader.GetOrdinal("AcceptRanges")),
                RawHeadersJson = reader.GetString(reader.GetOrdinal("RawHeadersJson")),
                RawCookiesJson = reader.GetString(reader.GetOrdinal("RawCookiesJson")),
                RawClientHintsJson = reader.GetString(reader.GetOrdinal("RawClientHintsJson")),
                RawTabJson = reader.GetString(reader.GetOrdinal("RawTabJson")),
                RawRedirectChainJson = reader.GetString(reader.GetOrdinal("RawRedirectChainJson")),
                RawBrowserHeadersJson = reader.IsDBNull(reader.GetOrdinal("RawBrowserHeadersJson")) ? "[]" : reader.GetString(reader.GetOrdinal("RawBrowserHeadersJson")),
                RawPostDataJson = reader.IsDBNull(reader.GetOrdinal("RawPostDataJson")) ? "{}" : reader.GetString(reader.GetOrdinal("RawPostDataJson")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt")))
            };
        }
    }
}
