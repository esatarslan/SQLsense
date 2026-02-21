using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SQLsense.Core
{
    public class SqlFormatterOptions
    {
        public SqlVersion SqlVersion { get; set; } = SqlVersion.Sql160;
        public KeywordCasing KeywordCasing { get; set; } = KeywordCasing.Uppercase;
        public int IndentationSize { get; set; } = 4;
        public bool AlignClauseBodies { get; set; } = true;
        public bool AsKeywordOnOwnLine { get; set; } = false;
        public bool IncludeSemicolons { get; set; } = true;
        public bool NewLineBeforeFromClause { get; set; } = true;
        public bool NewLineBeforeWhereClause { get; set; } = true;
        public bool NewLineBeforeJoinClause { get; set; } = true;
        public bool NewLineBeforeOrderByClause { get; set; } = true;
        public bool NewLineBeforeOffsetClause { get; set; } = true;
        public bool NewLineBeforeOutputClause { get; set; } = true;
    }
}
