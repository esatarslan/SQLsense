using System;
using System.Collections.Generic;
using System.Linq;

namespace SQLsense.Core
{
    public static class KeywordManager
    {
        private static readonly HashSet<string> _keywords;

        static KeywordManager()
        {
            var list = new[]
            {
                "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUTHORIZATION", "BACKUP", "BEGIN", "BETWEEN", "BREAK", "BROWSE", "BULK", "BY", 
                "CASCADE", "CASE", "CHECK", "CHECKPOINT", "CLOSE", "CLUSTERED", "COALESCE", "COLLATE", "COLUMN", "COMMIT", "COMPUTE", "CONSTRAINT", 
                "CONTAINS", "CONTAINSTABLE", "CONTINUE", "CONVERT", "CREATE", "CROSS", "CURRENT", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", 
                "CURRENT_USER", "CURSOR", "DATABASE", "DBCC", "DEALLOCATE", "DECLARE", "DEFAULT", "DELETE", "DENY", "DESC", "DISK", "DISTINCT", 
                "DISTRIBUTED", "DOUBLE", "DROP", "DUMMY", "DUMP", "ELSE", "END", "ERRLVL", "EXCEPT", "EXEC", "EXECUTE", "EXISTS", "EXIT", "FETCH", 
                "FILE", "FILLFACTOR", "FOR", "FOREIGN", "FREETEXT", "FREETEXTTABLE", "FROM", "FULL", "FUNCTION", "GOTO", "GRANT", "GROUP", "HAVING", 
                "HOLDLOCK", "IDENTITY", "IDENTITY_INSERT", "IDENTITYCOL", "IF", "IN", "INDEX", "INNER", "INSERT", "INTERSECT", "INTO", "IS", "JOIN", 
                "KEY", "KILL", "LEFT", "LIKE", "LINENO", "LOAD", "NATIONAL", "NOCHECK", "NONCLUSTERED", "NOT", "NULL", "NULLIF", "OF", "OFF", 
                "OFFSETS", "ON", "OPEN", "OPENDATASOURCE", "OPENQUERY", "OPENROWSET", "OPENXML", "OPTION", "OR", "ORDER", "OUTER", "OVER", 
                "PERCENT", "PLAN", "PRECISION", "PRIMARY", "PRINT", "PROC", "PROCEDURE", "PUBLIC", "RAISERROR", "READ", "READTEXT", "RECONFIGURE", 
                "REFERENCES", "REPLICATION", "RESTORE", "RESTRICT", "RETURN", "REVOKE", "RIGHT", "ROLLBACK", "ROWCOUNT", "ROWGUIDCOL", "RULE", 
                "SAVE", "SCHEMA", "SELECT", "SESSION_USER", "SET", "SETUSER", "SHUTDOWN", "SOME", "STATISTICS", "SYSTEM_USER", "TABLE", "TEXTSIZE", 
                "THEN", "TO", "TOP", "TRAN", "TRANSACTION", "TRIGGER", "TRUNCATE", "TSEQUAL", "UNION", "UNIQUE", "UPDATE", "UPDATETEXT", "USE", 
                "USER", "VALUES", "VARYING", "VIEW", "WAITFOR", "WHEN", "WHERE", "WHILE", "WITH",
                "VARCHAR", "NVARCHAR", "INT", "BIGINT", "SMALLINT", "TINYINT", "BIT", "DECIMAL", "NUMERIC", "MONEY", "SMALLMONEY", "FLOAT", 
                "REAL", "DATETIME", "DATETIME2", "DATETIMEOFFSET", "DATE", "TIME", "CHAR", "NCHAR", "BINARY", "VARBINARY", "IMAGE", "TEXT", 
                "NTEXT", "XML", "GO", "PIVOT", "UNPIVOT", "MERGE", "OUTPUT"
            };

            _keywords = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsKeyword(string word)
        {
            return _keywords.Contains(word);
        }

        public static string GetCasedKeyword(string word)
        {
            if (_keywords.TryGetValue(word, out string originalWord))
            {
                // We use ToUpperInvariant to ensure standard 'I' in Turkish environments
                return word.ToUpperInvariant();
            }
            return word;
        }

        public static IEnumerable<string> AllKeywords => _keywords;
    }
}
