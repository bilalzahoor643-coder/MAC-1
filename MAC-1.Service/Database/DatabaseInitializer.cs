using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace MAC_1.Service.Database
{
    public class DatabaseInitializer
    {
        private readonly string _connectionString;
        private const int CURRENT_SCHEMA_VERSION = 4;

        private static readonly List<Action<SqliteConnection>> Migrations = new()
        {
            RunMigration1,
            RunMigration2,
            RunMigration3,
            RunMigration4
        };

        public DatabaseInitializer(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        public void Initialize()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            ExecuteNonQuery(connection, "PRAGMA journal_mode=WAL");
            ExecuteNonQuery(connection, "PRAGMA synchronous=NORMAL");
            ExecuteNonQuery(connection, "PRAGMA busy_timeout=5000");

            CreateSchemaVersionTable(connection);
            int currentVersion = GetSchemaVersion(connection);

            if (currentVersion >= CURRENT_SCHEMA_VERSION)
            {
                Console.WriteLine($"[Database] Schema up to date: v{currentVersion}");
                return;
            }

            Console.WriteLine($"[Database] Migrating v{currentVersion} → v{CURRENT_SCHEMA_VERSION}");

            for (int v = currentVersion; v < CURRENT_SCHEMA_VERSION; v++)
            {
                RunMigration(connection, v + 1);
            }

            Console.WriteLine($"[Database] Migration complete: v{CURRENT_SCHEMA_VERSION}");
        }

        private void CreateSchemaVersionTable(SqliteConnection connection)
        {
            ExecuteNonQuery(connection, @"
                CREATE TABLE IF NOT EXISTS SchemaVersion (
                    Version INTEGER PRIMARY KEY,
                    AppliedAt TEXT NOT NULL DEFAULT (datetime('now'))
                )");
        }

        private int GetSchemaVersion(SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion";
            var result = cmd.ExecuteScalar();
            return Convert.ToInt32(result);
        }

        private void RunMigration(SqliteConnection connection, int version)
        {
            if (version < 1 || version > Migrations.Count)
            {
                Console.WriteLine($"[Database] Unknown migration v{version}, skipping");
                return;
            }

            using var transaction = connection.BeginTransaction();
            try
            {
                Migrations[version - 1](connection);

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO SchemaVersion (Version) VALUES ($v)";
                cmd.Parameters.AddWithValue("$v", version);
                cmd.ExecuteNonQuery();

                transaction.Commit();
                Console.WriteLine($"[Database] Migration v{version} applied");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"[Database] Migration v{version} FAILED: {ex.Message}");
                throw;
            }
        }

        private static void RunMigration1(SqliteConnection connection)
        {
            ExecuteNonQuery(connection, @"
                CREATE TABLE IF NOT EXISTS DownloadSessions (
                    SessionId TEXT PRIMARY KEY,
                    Url TEXT NOT NULL,
                    FinalUrl TEXT NOT NULL DEFAULT '',
                    Filename TEXT NOT NULL,
                    FileExtension TEXT NOT NULL DEFAULT '',
                    FileSize INTEGER NOT NULL DEFAULT 0,
                    MimeType TEXT NOT NULL DEFAULT '',
                    Referrer TEXT NOT NULL DEFAULT '',
                    Origin TEXT NOT NULL DEFAULT '',
                    RequestMethod TEXT NOT NULL DEFAULT 'GET',
                    UserAgent TEXT NOT NULL DEFAULT '',
                    Host TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT 'pending',
                    SavePath TEXT NOT NULL DEFAULT '',
                    Category TEXT NOT NULL DEFAULT 'General',
                    BytesDownloaded INTEGER NOT NULL DEFAULT 0,
                    Connections INTEGER NOT NULL DEFAULT 1,
                    ResumeSupported INTEGER NOT NULL DEFAULT 1,
                    ETag TEXT,
                    LastModified TEXT,
                    AcceptRanges TEXT,
                    RawHeadersJson TEXT NOT NULL DEFAULT '{}',
                    RawCookiesJson TEXT NOT NULL DEFAULT '[]',
                    RawClientHintsJson TEXT NOT NULL DEFAULT '{}',
                    RawTabJson TEXT NOT NULL DEFAULT '{}',
                    RawRedirectChainJson TEXT NOT NULL DEFAULT '[]',
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                    UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
                )");

            ExecuteNonQuery(connection, @"
                CREATE INDEX IF NOT EXISTS IX_DownloadSessions_Status 
                ON DownloadSessions(Status)");

            ExecuteNonQuery(connection, @"
                CREATE INDEX IF NOT EXISTS IX_DownloadSessions_CreatedAt 
                ON DownloadSessions(CreatedAt)");

            ExecuteNonQuery(connection, @"
                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
                )");

            ExecuteNonQuery(connection, @"
                CREATE TABLE IF NOT EXISTS History (
                    SessionId TEXT PRIMARY KEY,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT,
                    Result TEXT NOT NULL DEFAULT '',
                    FinalPath TEXT NOT NULL DEFAULT '',
                    FinalSize INTEGER NOT NULL DEFAULT 0,
                    ErrorMessage TEXT,
                    FOREIGN KEY (SessionId) REFERENCES DownloadSessions(SessionId)
                )");
        }

        private static void ExecuteNonQuery(SqliteConnection connection, string sql)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static void RunMigration2(SqliteConnection connection)
        {
            // Add progress tracking columns for download engine
            string[] columns = {
                "ALTER TABLE DownloadSessions ADD COLUMN Progress REAL NOT NULL DEFAULT 0",
                "ALTER TABLE DownloadSessions ADD COLUMN Speed REAL NOT NULL DEFAULT 0",
                "ALTER TABLE DownloadSessions ADD COLUMN AverageSpeed REAL NOT NULL DEFAULT 0",
                "ALTER TABLE DownloadSessions ADD COLUMN ETA REAL NOT NULL DEFAULT 0",
                "ALTER TABLE DownloadSessions ADD COLUMN ErrorMessage TEXT",
                "ALTER TABLE DownloadSessions ADD COLUMN FinalUrl TEXT NOT NULL DEFAULT ''",
                "ALTER TABLE DownloadSessions ADD COLUMN HttpStatusCode INTEGER NOT NULL DEFAULT 0",
                "ALTER TABLE DownloadSessions ADD COLUMN ElapsedSeconds REAL NOT NULL DEFAULT 0"
            };

            foreach (var sql in columns)
            {
                try { ExecuteNonQuery(connection, sql); }
                catch { }
            }

            ExecuteNonQuery(connection, @"
                CREATE INDEX IF NOT EXISTS IX_DownloadSessions_Url 
                ON DownloadSessions(Url)");
        }

        private static void RunMigration3(SqliteConnection connection)
        {
            try { ExecuteNonQuery(connection, "ALTER TABLE DownloadSessions ADD COLUMN RawBrowserHeadersJson TEXT NOT NULL DEFAULT '[]'"); }
            catch { }
        }

        private static void RunMigration4(SqliteConnection connection)
        {
            try { ExecuteNonQuery(connection, "ALTER TABLE DownloadSessions ADD COLUMN RawPostDataJson TEXT NOT NULL DEFAULT '{}'"); }
            catch { }
        }
    }
}
