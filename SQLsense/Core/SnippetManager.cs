using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SQLsense.Infrastructure;

namespace SQLsense.Core
{
    public class SnippetManager
    {
        private Dictionary<string, string> _snippets;

        public SnippetManager()
        {
            LoadSnippets();
        }

        private void LoadSnippets()
        {
            try
            {
                // Set defaults
                _snippets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ssf", "SELECT * FROM " },
                    { "st100", "SELECT TOP 100 * FROM " },
                    { "sc", "SELECT COUNT(*) FROM " },
                    { "ct", "CREATE TABLE " },
                    { "ii", "INSERT INTO " },
                    { "ud", "UPDATE  SET  WHERE " },
                    { "df", "DELETE FROM  WHERE " },
                    { "gb", "GROUP BY " },
                    { "ob", "ORDER BY " },
                    { "go", "GO" + Environment.NewLine }
                };

                // Try to load from JSON file
                string assemblyPath = Path.GetDirectoryName(typeof(SnippetManager).Assembly.Location);
                string jsonPath = Path.Combine(assemblyPath, "snippets.json");

                if (File.Exists(jsonPath))
                {
                    string json = File.ReadAllText(jsonPath);
                    var externalSnippets = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    
                    if (externalSnippets != null)
                    {
                        foreach (var kvp in externalSnippets)
                        {
                            _snippets[kvp.Key] = kvp.Value;
                        }
                        OutputWindowLogger.Log($"Loaded {externalSnippets.Count} snippets from snippets.json");
                    }
                }
                else
                {
                    OutputWindowLogger.Log("snippets.json not found, using default snippets.");
                }
            }
            catch (Exception ex)
            {
                OutputWindowLogger.LogError("Failed to load snippets from JSON", ex);
            }
        }

        public bool TryGetSnippet(string shortcut, out string expansion)
        {
            return _snippets.TryGetValue(shortcut, out expansion);
        }

        public IEnumerable<string> AllShortcuts => _snippets.Keys;
        public IEnumerable<KeyValuePair<string, string>> AllSnippets => _snippets;
    }
}
