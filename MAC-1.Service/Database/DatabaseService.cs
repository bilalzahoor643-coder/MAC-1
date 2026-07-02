using System;
using System.IO;
using System.Text.Json;
using MAC_1.Service.Database.Models;
using MAC_1.Service.Database.Repositories;
using MAC_1.Service.Models;
using Microsoft.Data.Sqlite;

namespace MAC_1.Service.Database
{
    public class DatabaseService
    {
        private static readonly Lazy<DatabaseService> _instance = new(() => new DatabaseService());
        public static DatabaseService Instance => _instance.Value;

        private readonly string _dbDirectory;
        private readonly string _dbPath;
        private readonly string _connectionString;
        private readonly DatabaseInitializer _initializer;
        private readonly DownloadSessionRepository _sessions;
        private readonly SettingsRepository _settings;
        private readonly HistoryRepository _history;

        public DownloadSessionRepository Sessions => _sessions;
        public SettingsRepository Settings => _settings;
        public HistoryRepository History => _history;
        public string DbPath => _dbPath;

        private DatabaseService()
        {
            _dbDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MAC-1");
            Directory.CreateDirectory(_dbDirectory);

            _dbPath = Path.Combine(_dbDirectory, "downloads.db");
            _connectionString = $"Data Source={_dbPath}";

            _initializer = new DatabaseInitializer(_dbPath);
            _sessions = new DownloadSessionRepository(_connectionString);
            _settings = new SettingsRepository(_connectionString);
            _history = new HistoryRepository(_connectionString);
        }

        public void Initialize()
        {
            try
            {
                _initializer.Initialize();
                Console.WriteLine($"[Database] Initialized: {_dbPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Database] Init FAILED: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a new session entity from DownloadSession.
        /// SessionId is generated ONCE here and NEVER changes.
        /// </summary>
        public DownloadSessionEntity CreateSessionEntity(MAC_1.Service.Models.DownloadSession session)
        {
            return new DownloadSessionEntity
            {
                SessionId = Guid.NewGuid().ToString("N"),
                Url = session.Url,
                FinalUrl = session.FinalUrl,
                Filename = session.Filename,
                FileExtension = session.FileExtension,
                FileSize = session.FileSize,
                MimeType = session.MimeType,
                Referrer = session.Referrer,
                Origin = session.Origin,
                RequestMethod = session.RequestMethod,
                UserAgent = session.UserAgent,
                Host = session.Host,
                Status = "pending",
                SavePath = session.SavePath,
                Category = session.Category,
                ResumeSupported = session.ResumeSupported,
                ETag = string.IsNullOrEmpty(session.ETag) ? null : session.ETag,
                LastModified = string.IsNullOrEmpty(session.LastModified) ? null : session.LastModified,
                AcceptRanges = string.IsNullOrEmpty(session.AcceptRanges) ? null : session.AcceptRanges,
                RawHeadersJson = JsonSerializer.Serialize(session.Headers),
                RawCookiesJson = JsonSerializer.Serialize(session.Cookies),
                RawClientHintsJson = JsonSerializer.Serialize(session.ClientHints ?? new()),
                RawTabJson = JsonSerializer.Serialize(session.Tab ?? new()),
                RawRedirectChainJson = JsonSerializer.Serialize(session.RedirectChain),
                RawBrowserHeadersJson = JsonSerializer.Serialize(session.BrowserRawHeaders ?? new()),
                RawPostDataJson = session.PostData != null ? JsonSerializer.Serialize(session.PostData) : "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Transactional save: DownloadSession + History in one atomic write.
        /// </summary>
        public DownloadSessionEntity SaveSessionTransactional(MAC_1.Service.Models.DownloadSession session)
        {
            var entity = CreateSessionEntity(session);

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                try
                {
                    _sessions.Save(entity, connection);

                    _history.Save(new HistoryEntity
                    {
                        SessionId = entity.SessionId,
                        StartTime = DateTime.UtcNow,
                        Result = "pending",
                        FinalPath = session.SavePath
                    }, connection);

                    transaction.Commit();
                    Console.WriteLine($"[Database] Transactional save: {entity.SessionId} ({entity.Filename})");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine($"[Database] Transaction ROLLED BACK: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Database] Save FAILED: {ex.Message}");
            }

            return entity;
        }

        /// <summary>
        /// Legacy method for backward compatibility.
        /// Uses transactional save internally.
        /// </summary>
        public DownloadSessionEntity SaveSessionFromDownloadSession(MAC_1.Service.Models.DownloadSession session)
        {
            return SaveSessionTransactional(session);
        }

        /// <summary>
        /// Transactional status update: Session + History in one atomic write.
        /// </summary>
        public void UpdateStatusTransactional(string sessionId, string status, string? finalPath = null,
            long bytesDownloaded = 0, long fileSize = 0, double progress = 0, string? errorMessage = null)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                try
                {
                    _sessions.UpdateStatusFull(sessionId, status, finalPath,
                        bytesDownloaded, fileSize, progress, errorMessage, connection);

                    if (status == "completed" || status == "failed")
                    {
                        _history.Save(new HistoryEntity
                        {
                            SessionId = sessionId,
                            StartTime = DateTime.UtcNow,
                            EndTime = DateTime.UtcNow,
                            Result = status,
                            FinalPath = finalPath ?? string.Empty,
                            FinalSize = fileSize,
                            ErrorMessage = errorMessage
                        }, connection);
                    }

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine($"[Database] Status update ROLLED BACK: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Database] Status update FAILED: {ex.Message}");
            }
        }

        /// <summary>
        /// Full progress update: bytes, size, progress, speed, average speed, ETA, save path.
        /// </summary>
        public void UpdateProgressTransactional(string sessionId, long bytesDownloaded, long fileSize,
            double progress, double speed, double averageSpeed, double eta, string savePath)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                try
                {
                    _sessions.UpdateProgress(sessionId, bytesDownloaded, fileSize, progress,
                        speed, averageSpeed, eta, savePath, connection);
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine($"[Database] Progress update ROLLED BACK: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Database] Progress update FAILED: {ex.Message}");
            }
        }

        /// <summary>
        /// Metadata update: file size, resume support, HTTP status, final URL.
        /// </summary>
        public void UpdateMetadataTransactional(string sessionId, long fileSize, bool resumeSupported,
            int httpStatusCode, string? finalUrl)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                try
                {
                    _sessions.UpdateMetadata(sessionId, fileSize, resumeSupported,
                        httpStatusCode, finalUrl, connection);
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine($"[Database] Metadata update ROLLED BACK: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Database] Metadata update FAILED: {ex.Message}");
            }
        }
    }
}
