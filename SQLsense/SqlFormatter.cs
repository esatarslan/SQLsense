using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SQLsense
{
    public class SqlFormatter
    {
        private readonly TSql160Parser _parser;
        private readonly Sql160ScriptGenerator _generator;

        public SqlFormatter()
        {
            _parser = new TSql160Parser(true);

            var options = new SqlScriptGeneratorOptions
            {
                SqlVersion = SqlVersion.Sql160,
                KeywordCasing = KeywordCasing.Uppercase,
                IndentationSize = 4,
                AlignClauseBodies = true,
                AsKeywordOnOwnLine = false,
                IncludeSemicolons = true,
                NewLineBeforeFromClause = true,
                NewLineBeforeJoinClause = true,
                NewLineBeforeOffsetClause = true,
                NewLineBeforeOutputClause = true,
                NewLineBeforeOrderByClause = true,
                NewLineBeforeWhereClause = true
            };

            _generator = new Sql160ScriptGenerator(options);
        }

        public string Format(string sql, out IList<ParseError> errors)
        {
            errors = null;
            if (string.IsNullOrWhiteSpace(sql)) return sql;

            using (var reader = new StringReader(sql))
            {
                var fragment = _parser.Parse(reader, out errors);

                if (errors != null && errors.Count > 0)
                {
                    return null;
                }

                _generator.GenerateScript(fragment, out string formattedSql);
                return formattedSql;
            }
        }
    }
}
