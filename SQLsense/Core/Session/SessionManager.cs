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
                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Sessions (
                        Id TEXT PRIMARY KEY,
                        FilePath TEXT,
                        Content TEXT,
                        LastUpdated DATETIME
                    )";
                command.ExecuteNonQuery();
            }
        }

        public void SaveSession(SessionEntry entry)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Sessions (Id, FilePath, Content, LastUpdated)
                    VALUES ($id, $path, $content, $date)
                    ON CONFLICT(Id) DO UPDATE SET
                        Content = excluded.Content,
                        LastUpdated = excluded.LastUpdated";
                
                command.Parameters.AddWithValue("$id", entry.Id);
                command.Parameters.AddWithValue("$path", entry.FilePath ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$content", entry.Content ?? string.Empty);
                command.Parameters.AddWithValue("$date", entry.LastUpdated);
                
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
                command.CommandText = "SELECT Id, FilePath, Content, LastUpdated FROM Sessions";
                
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        sessions.Add(new SessionEntry
                        {
                            Id = reader.GetString(0),
                            FilePath = reader.IsDBNull(1) ? null : reader.GetString(1),
                            Content = reader.GetString(2),
                            LastUpdated = reader.GetDateTime(3)
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
