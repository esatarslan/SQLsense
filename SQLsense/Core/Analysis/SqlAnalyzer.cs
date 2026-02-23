using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SQLsense.Core.Analysis
{
    public class SqlAnalyzer : ISqlAnalyzer
    {
        private readonly TSql160Parser _parser;

        public SqlAnalyzer()
        {
            _parser = new TSql160Parser(true);
        }

        public IEnumerable<SqlAnalysisResult> Analyze(string sql)
        {
            var results = new List<SqlAnalysisResult>();
            if (string.IsNullOrWhiteSpace(sql)) return results;

            try
            {
                using (var reader = new StringReader(sql))
                {
                    var fragment = _parser.Parse(reader, out var errors);

                    // 1. Basic Syntax Errors from ScriptDom
                    if (errors != null && errors.Count > 0)
                    {
                        foreach (var error in errors)
                        {
                            results.Add(new SqlAnalysisResult
                            {
                                Message = error.Message,
                                Line = error.Line,
                                Column = error.Column,
                                Severity = AnalysisSeverity.Error,
                                RuleId = "SYNTAX"
                            });
                        }
                        // If there are syntax errors, semantic analysis might be unreliable
                        return results;
                    }

                    // 2. Custom Rule Analysis using Visitor
                    var visitor = new SQLsenseVisitor();
                    fragment.Accept(visitor);
                    results.AddRange(visitor.Results);
                }
            }
            catch (Exception ex)
            {
                Infrastructure.OutputWindowLogger.LogError("Analysis failed", ex);
            }

            return results;
        }
    }

    internal class SQLsenseVisitor : TSqlFragmentVisitor
    {
        public List<SqlAnalysisResult> Results { get; } = new List<SqlAnalysisResult>();

        // Rule: Detect SELECT *
        public override void ExplicitVisit(SelectStarExpression node)
        {
            Results.Add(new SqlAnalysisResult
            {
                RuleId = "SS001",
                Message = "Consider specifying column names instead of '*' for better performance and maintainability.",
                Line = node.StartLine,
                Column = node.StartColumn,
                Severity = AnalysisSeverity.Warning
            });
            base.ExplicitVisit(node);
        }

        // Rule: Detect DELETE without WHERE
        public override void ExplicitVisit(DeleteStatement node)
        {
            if (node.DeleteSpecification.WhereClause == null)
            {
                Results.Add(new SqlAnalysisResult
                {
                    RuleId = "SS002",
                    Message = "DELETE statement without a WHERE clause will delete all rows in the table!",
                    Line = node.StartLine,
                    Column = node.StartColumn,
                    Severity = AnalysisSeverity.Error
                });
            }
            base.ExplicitVisit(node);
        }

        // Rule: Detect UPDATE without WHERE
        public override void ExplicitVisit(UpdateStatement node)
        {
            if (node.UpdateSpecification.WhereClause == null)
            {
                Results.Add(new SqlAnalysisResult
                {
                    RuleId = "SS003",
                    Message = "UPDATE statement without a WHERE clause will update all rows in the table!",
                    Line = node.StartLine,
                    Column = node.StartColumn,
                    Severity = AnalysisSeverity.Error
                });
            }
            base.ExplicitVisit(node);
        }
    }
}
