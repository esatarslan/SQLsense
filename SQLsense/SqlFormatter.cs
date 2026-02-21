using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SQLsense.Core;
using SQLsense.Infrastructure;

namespace SQLsense
{
    public class SqlFormatter : ISqlFormatter
    {
        private readonly TSql160Parser _parser;
        private readonly Sql160ScriptGenerator _generator;

        public SqlFormatter(SqlFormatterOptions options = null)
        {
            options = options ?? new SqlFormatterOptions();
            _parser = new TSql160Parser(true);

            var generatorOptions = new SqlScriptGeneratorOptions
            {
                SqlVersion = options.SqlVersion,
                KeywordCasing = options.KeywordCasing,
                IndentationSize = options.IndentationSize,
                AlignClauseBodies = options.AlignClauseBodies,
                AsKeywordOnOwnLine = options.AsKeywordOnOwnLine,
                IncludeSemicolons = options.IncludeSemicolons,
                NewLineBeforeFromClause = options.NewLineBeforeFromClause,
                NewLineBeforeJoinClause = options.NewLineBeforeJoinClause,
                NewLineBeforeOffsetClause = options.NewLineBeforeOffsetClause,
                NewLineBeforeOutputClause = options.NewLineBeforeOutputClause,
                NewLineBeforeOrderByClause = options.NewLineBeforeOrderByClause,
                NewLineBeforeWhereClause = options.NewLineBeforeWhereClause
            };

            _generator = new Sql160ScriptGenerator(generatorOptions);
        }

        public string Format(string sql, out IList<ParseError> errors)
        {
            errors = null;
            if (string.IsNullOrWhiteSpace(sql)) return sql;

            try
            {
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
            catch (Exception ex)
            {
                OutputWindowLogger.LogError("Formatting failed", ex);
                return null;
            }
        }
    }
}
