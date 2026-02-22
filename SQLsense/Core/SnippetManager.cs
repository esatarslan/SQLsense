using System;
using System.Collections.Generic;

namespace SQLsense.Core
{
    public class SnippetManager
    {
        private readonly Dictionary<string, string> _snippets;

        public SnippetManager()
        {
            // Default snippets - In a later phase, these will be loaded from a JSON file/settings
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
        }

        public bool TryGetSnippet(string shortcut, out string expansion)
        {
            return _snippets.TryGetValue(shortcut, out expansion);
        }

        public IEnumerable<string> AllShortcuts => _snippets.Keys;
    }
}
