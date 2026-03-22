using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace SQLsense.Core.Session
{
    public class SessionEntry
    {
        public string Id { get; set; }
        public string FilePath { get; set; }
        public string Content { get; set; }
        public DateTime LastUpdated { get; set; }

        // Connection info (no password stored — Windows Auth needs only server/db)
        public string ServerName { get; set; }
        public string DatabaseName { get; set; }
        public int AuthType { get; set; }   // 0 = Windows, 1 = SQL Server
        public string UserName { get; set; }
    }

    public class SessionManager
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public SessionManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string sqlSensePath = Path.Combine(appData, "SQLsense");
            if (!Directory.Exists(sqlSensePath))
            {
                Directory.CreateDirectory(sqlSensePath);
            }

            _dbPath = Path.Combine(sqlSensePath, "sessions.db");
            _connectionString = $"Data Source={_dbPath}";
            
            // Initialize SQLite native provider
            SQLitePCL.Batteries.Init();
            
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                // Create base table if not exists
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Sessions (
                        Id TEXT PRIMARY KEY,
                        FilePath TEXT,
                        Content TEXT,
                        LastUpdated DATETIME,
                        ServerName TEXT,
                        DatabaseName TEXT,
                        AuthType INTEGER DEFAULT 0,
                        UserName TEXT
                    )";
                cmd.ExecuteNonQuery();

                // Migrate existing DB: add columns if missing (SQLite supports ADD COLUMN safely)
                foreach (var col in new[] {
                    "ServerName TEXT",
                    "DatabaseName TEXT",
                    "AuthType INTEGER DEFAULT 0",
                    "UserName TEXT"
                })
                {
                    try
                    {
                        var migrate = connection.CreateCommand();
                        migrate.CommandText = $"ALTER TABLE Sessions ADD COLUMN {col}";
                        migrate.ExecuteNonQuery();
                    }
                    catch { /* Column already exists — ignore */ }
                }
            }
        }

        public void SaveSession(SessionEntry entry)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Sessions (Id, FilePath, Content, LastUpdated, ServerName, DatabaseName, AuthType, UserName)
                    VALUES ($id, $path, $content, $date, $server, $db, $authType, $user)
                    ON CONFLICT(Id) DO UPDATE SET
                        Content = excluded.Content,
                        LastUpdated = excluded.LastUpdated,
                        ServerName = excluded.ServerName,
                        DatabaseName = excluded.DatabaseName,
                        AuthType = excluded.AuthType,
                        UserName = excluded.UserName";
                
                command.Parameters.AddWithValue("$id", entry.Id);
                command.Parameters.AddWithValue("$path", entry.FilePath ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$content", entry.Content ?? string.Empty);
                command.Parameters.AddWithValue("$date", entry.LastUpdated);
                command.Parameters.AddWithValue("$server", entry.ServerName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$db", entry.DatabaseName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$authType", entry.AuthType);
                command.Parameters.AddWithValue("$user", entry.UserName ?? (object)DBNull.Value);
                
                command.ExecuteNonQuery();
            }
        }

        public void DeleteSession(string sessionId)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Sessions WHERE Id = $id";
                command.Parameters.AddWithValue("$id", sessionId);
                command.ExecuteNonQuery();
            }
        }

        public List<SessionEntry> GetAllSessions()
        {
            var sessions = new List<SessionEntry>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, FilePath, Content, LastUpdated, ServerName, DatabaseName, AuthType, UserName FROM Sessions";
                
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        sessions.Add(new SessionEntry
                        {
                            Id           = reader.GetString(0),
                            FilePath     = reader.IsDBNull(1) ? null : reader.GetString(1),
                            Content      = reader.GetString(2),
                            LastUpdated  = reader.GetDateTime(3),
                            ServerName   = reader.IsDBNull(4) ? null : reader.GetString(4),
                            DatabaseName = reader.IsDBNull(5) ? null : reader.GetString(5),
                            AuthType     = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                            UserName     = reader.IsDBNull(7) ? null : reader.GetString(7),
                        });
                    }
                }
            }
            return sessions;
        }

        public void ClearAll()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Sessions";
                command.ExecuteNonQuery();
            }
        }
    }
}

