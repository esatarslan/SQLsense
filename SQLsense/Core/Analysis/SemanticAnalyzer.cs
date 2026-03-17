using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SQLsense.Core.Analysis
{
    public enum AliasSourceType
    {
        Table,      // Represents a physical Base Table or View
        Projected   // Represents a DerivedTable (Subquery) or CTE with specific columns
    }

    public class AliasDefinition
    {
        public AliasSourceType SourceType { get; set; }
        public string AliasName { get; set; }
        public string BoundObjectName { get; set; } // TableName for base tables, null for subqueries unless resolved
        public List<string> ProjectedColumns { get; } = new List<string>();
        public bool IsStarExpanded { get; set; }
        public int ScopeStartOffset { get; set; }
        public int ScopeEndOffset { get; set; }
    }

    /// <summary>
    /// Attempts to repair broken syntaxes (like "SELECT t0. FROM ...") by substituting placeholders,
    /// so the ScriptDom parser can generate a valid AST tree revealing subqueries and CTEs.
    /// </summary>
    public static class AstHealer
    {
        public static TSqlFragment HealAndParse(string sqlText, out IList<ParseError> errors)
        {
            var parser = new TSql160Parser(true);
            
            // First try parsing unmodified SQL
            var fragment = parser.Parse(new StringReader(sqlText), out errors);
            if (errors.Count == 0 && IsValidSelectTree(fragment))
            {
                return fragment;
            }

            // Heuristic 1: Incomplete Select List with "FROM" immediately following
            // "SELECT t0. FROM" -> "SELECT 1 FROM"
            string healedSql = sqlText;
            bool modified = false;

            // Heuristic 0: Empty SELECT list — "SELECT  FROM" → "SELECT 1 FROM"
            // When cursor is between SELECT and FROM, the SELECT list is empty causing parse failure.
            // We pad with "1" to keep offsets intact for offset-based analysis.
            {
                int idx = 0;
                while (idx < healedSql.Length)
                {
                    int selectPos = healedSql.IndexOf("SELECT", idx, StringComparison.OrdinalIgnoreCase);
                    if (selectPos < 0) break;
                    
                    int afterSelect = selectPos + 6; // length of "SELECT"
                    // Find the next non-whitespace after SELECT
                    int nextNonWs = afterSelect;
                    while (nextNonWs < healedSql.Length && (healedSql[nextNonWs] == ' ' || healedSql[nextNonWs] == '\t' || healedSql[nextNonWs] == '\n' || healedSql[nextNonWs] == '\r'))
                    {
                        nextNonWs++;
                    }
                    
                    // Check if the next meaningful token is FROM
                    if (nextNonWs + 4 <= healedSql.Length && healedSql.Substring(nextNonWs, 4).Equals("FROM", StringComparison.OrdinalIgnoreCase))
                    {
                        // Replace the whitespace between SELECT and FROM with " 1 " padded to same length
                        int gapLen = nextNonWs - afterSelect;
                        if (gapLen >= 2)
                        {
                            string filler = " 1" + new string(' ', gapLen - 2);
                            healedSql = healedSql.Substring(0, afterSelect) + filler + healedSql.Substring(nextNonWs);
                            modified = true;
                        }
                    }
                    
                    idx = afterSelect;
                }
            }

            // Simple fast-path for trailing dot before FROM/WHERE/JOIN
            int lastDotFrom = healedSql.LastIndexOf(". FROM", StringComparison.OrdinalIgnoreCase);
            if (lastDotFrom > 0)
            {
                int selectIdx = healedSql.LastIndexOf("SELECT", lastDotFrom, StringComparison.OrdinalIgnoreCase);
                if (selectIdx >= 0)
                {
                    // Ex: "SELECT a, b, t0. FROM " -> "SELECT 1 FROM "
                    // To preserve absolute offsets for the rest of the query, we pad with spaces
                    int lenToReplace = (lastDotFrom + 1) - selectIdx;
                    string replacement = "SELECT 1".PadRight(lenToReplace, ' ');
                    healedSql = healedSql.Substring(0, selectIdx) + replacement + healedSql.Substring(lastDotFrom + 1);
                    modified = true;
                }
            }
            
            int lastDotJoin = healedSql.LastIndexOf(". JOIN", StringComparison.OrdinalIgnoreCase);
            if (lastDotJoin > 0)
            {
                 int onIdx = healedSql.LastIndexOf("ON ", lastDotJoin, StringComparison.OrdinalIgnoreCase);
                 if (onIdx >= 0 && onIdx < lastDotJoin)
                 {
                     int lenToReplace = (lastDotJoin + 1) - onIdx;
                     string replacement = "ON 1=1".PadRight(lenToReplace, ' ');
                     healedSql = healedSql.Substring(0, onIdx) + replacement + healedSql.Substring(lastDotJoin + 1);
                     modified = true;
                 }
            }

            int lastDotWhere = healedSql.LastIndexOf(". WHERE", StringComparison.OrdinalIgnoreCase);
            if (lastDotWhere > 0)
            {
                 int whereIdx = healedSql.LastIndexOf("WHERE ", lastDotWhere, StringComparison.OrdinalIgnoreCase);
                 if (whereIdx >= 0 && whereIdx < lastDotWhere)
                 {
                     int lenToReplace = (lastDotWhere + 1) - whereIdx;
                     string replacement = "WHERE 1=1".PadRight(lenToReplace, ' ');
                     healedSql = healedSql.Substring(0, whereIdx) + replacement + healedSql.Substring(lastDotWhere + 1);
                     modified = true;
                 }
            }

            string trimmedEnd = healedSql.TrimEnd();
            if (trimmedEnd.EndsWith("SET", StringComparison.OrdinalIgnoreCase))
            {
                healedSql = healedSql + " __dummy=1";
                modified = true;
            }
            else if (trimmedEnd.EndsWith("ON", StringComparison.OrdinalIgnoreCase) || trimmedEnd.EndsWith("WHERE", StringComparison.OrdinalIgnoreCase))
            {
                healedSql = healedSql + " 1=1";
                modified = true;
            }
            else if (trimmedEnd.EndsWith(","))
            {
                healedSql = healedSql + " __dummy=1";
                modified = true;
            }

            // Just trailing dot at the very end of snippet
            if (healedSql.TrimEnd().EndsWith("."))
            {
                healedSql = healedSql.Substring(0, healedSql.LastIndexOf('.')) + " ";
                modified = true;
            }

            if (modified)
            {
                var fallbackFragment = parser.Parse(new StringReader(healedSql), out IList<ParseError> fallbackErrors);
                // Even if healed has errors, returning the healed fragment is often better since it might contain the DerivedTable
                return fallbackFragment;
            }

            return fragment;
        }

        private static bool IsValidSelectTree(TSqlFragment fragment)
        {
            if (fragment is TSqlScript script && script.Batches.Count > 0)
            {
                foreach (var batch in script.Batches)
                {
                    foreach(var stmt in batch.Statements)
                    {
                        if (stmt is SelectStatement ss && ss.QueryExpression is QuerySpecification qs)
                        {
                            return qs.SelectElements.Count > 0 && qs.FromClause != null;
                        }
                    }
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Sweeps the AST to identify all active scopes (CTEs, Subqueries, Base Tables)
    /// relative to the user's caret position.
    /// </summary>
    public class ScopeAnalyzerVisitor : TSqlFragmentVisitor
    {
        private readonly int _caretPosition;
        private readonly Dictionary<string, AliasDefinition> _activeAliases = new Dictionary<string, AliasDefinition>(StringComparer.OrdinalIgnoreCase);
        
        // Track the current bounds for inner scopes
        private readonly Stack<(int start, int end)> _scopeStack = new Stack<(int, int)>();

        public IReadOnlyDictionary<string, AliasDefinition> ActiveAliases => _activeAliases;

        public ScopeAnalyzerVisitor(int caretPosition)
        {
            _caretPosition = caretPosition;
        }

        public override void ExplicitVisit(QuerySpecification node)
        {
            int start = node.ScriptTokenStream[node.FirstTokenIndex].Offset;
            int end = node.ScriptTokenStream[node.LastTokenIndex].Offset + node.ScriptTokenStream[node.LastTokenIndex].Text.Length;
            
            _scopeStack.Push((start, end));
            
            // Only process inside if caret is within this query specification bounds (with some margin for trailing text)
            if (IsCaretInCurrentScope())
            {
                 base.ExplicitVisit(node);
            }
            else
            {
                 // We don't visit the children deeply if we are fully outside of this query spec, 
                 // UNLESS it's a CTE or DerivedTable that we need to extract projections from (handled by those specific Visit methods).
                 base.ExplicitVisit(node); 
            }
            
            _scopeStack.Pop();
        }

        public override void ExplicitVisit(NamedTableReference node)
        {
            if (IsCaretInCurrentScope() && node.SchemaObject != null && node.SchemaObject.BaseIdentifier != null)
            {
                string tableName = node.SchemaObject.BaseIdentifier.Value;
                string aliasName = node.Alias != null ? node.Alias.Value : tableName;

                if (!_activeAliases.ContainsKey(aliasName))
                {
                    _activeAliases[aliasName] = new AliasDefinition
                    {
                        SourceType = AliasSourceType.Table,
                        AliasName = aliasName,
                        BoundObjectName = tableName,
                        ScopeStartOffset = _scopeStack.Count > 0 ? _scopeStack.Peek().start : 0,
                        ScopeEndOffset = _scopeStack.Count > 0 ? _scopeStack.Peek().end : int.MaxValue
                    };
                }
            }
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(QueryDerivedTable node)
        {
            // A derived table (subquery) defines an alias for the outer scope.
            // But its body is parsed as an inner scope. We must visit the node to allow inner tables to resolve,
            // AND we must extract its projection for the outer scope if the outer scope is active.
            
            if (node.Alias != null && IsCaretInCurrentScope())
            {
                string aliasName = node.Alias.Value;
                var def = new AliasDefinition
                {
                    SourceType = AliasSourceType.Projected,
                    AliasName = aliasName,
                    ScopeStartOffset = _scopeStack.Count > 0 ? _scopeStack.Peek().start : 0,
                    ScopeEndOffset = _scopeStack.Count > 0 ? _scopeStack.Peek().end : int.MaxValue
                };

                // Extract projections using our dedicated extractor
                if (node.QueryExpression != null)
                {
                    var extractor = new ProjectionExtractorVisitor();
                    node.QueryExpression.Accept(extractor);
                    def.ProjectedColumns.AddRange(extractor.ProjectedColumns);
                    def.IsStarExpanded = extractor.IsStarExpanded;
                    def.BoundObjectName = extractor.FirstTableName;
                }

                if (!_activeAliases.ContainsKey(aliasName))
                {
                    _activeAliases[aliasName] = def;
                }
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CommonTableExpression node)
        {
            // CTEs define aliases available to the entire statement that follows.
            // Extract the projection.
            string cteName = node.ExpressionName?.Value;
            if (!string.IsNullOrEmpty(cteName) && node.QueryExpression != null)
            {
                 var def = new AliasDefinition
                 {
                     SourceType = AliasSourceType.Projected,
                     AliasName = cteName,
                     ScopeStartOffset = 0, // CTEs are broadly available in the following statement
                     ScopeEndOffset = int.MaxValue
                 };
                 
                 var extractor = new ProjectionExtractorVisitor();
                 node.QueryExpression.Accept(extractor);
                 def.ProjectedColumns.AddRange(extractor.ProjectedColumns);
                 def.IsStarExpanded = extractor.IsStarExpanded;
                 def.BoundObjectName = extractor.FirstTableName;

                 _activeAliases[cteName] = def;
            }
            
            base.ExplicitVisit(node);
        }

        private bool IsCaretInCurrentScope()
        {
            if (_scopeStack.Count == 0) return true; // Global scope
            var scope = _scopeStack.Peek();
            // Margin of +2 for spaces/newlines after the end token
            return _caretPosition >= scope.start && _caretPosition <= (scope.end + 2);
        }
    }

    /// <summary>
    /// Runs specifically over a QueryExpression (like the inner body of a DerivedTable or CTE) 
    /// and extracts the names of the columns that this expression "projects" (exports).
    /// </summary>
    public class ProjectionExtractorVisitor : TSqlFragmentVisitor
    {
        public List<string> ProjectedColumns { get; } = new List<string>();
        public bool IsStarExpanded { get; private set; }
        public string FirstTableName { get; private set; }

        public override void ExplicitVisit(QuerySpecification node)
        {
            foreach (var element in node.SelectElements)
            {
                if (element is SelectStarExpression)
                {
                    IsStarExpanded = true;
                    AttemptTableExtraction(node);
                    break;
                }
                else if (element is SelectScalarExpression sse)
                {
                    if (sse.ColumnName != null)
                    {
                        // Explicit alias: SELECT Kolon AS AliasedCol
                        ProjectedColumns.Add(sse.ColumnName.Value);
                    }
                    else
                    {
                        // Implicit column: SELECT T.Kolon
                        ExtractImplicitColumnName(sse.Expression);
                    }
                }
            }
            
            // Do not call base.ExplicitVisit because we only want the top-level projection, 
            // going deeper would extract columns from nested subqueries which aren't projected here.
        }

        public override void ExplicitVisit(BinaryQueryExpression node)
        {
            // For UNION/INTERSECT/EXCEPT, the projected columns are defined strictly by the FirstQueryExpression
            if (node.FirstQueryExpression != null)
            {
                node.FirstQueryExpression.Accept(this);
            }
        }

        private void ExtractImplicitColumnName(ScalarExpression expr)
        {
            if (expr is ColumnReferenceExpression cre && cre.MultiPartIdentifier != null && cre.MultiPartIdentifier.Identifiers.Count > 0)
            {
                ProjectedColumns.Add(cre.MultiPartIdentifier.Identifiers.Last().Value);
            }
        }

        private void AttemptTableExtraction(QuerySpecification node)
        {
            if (node.FromClause == null) return;
            
            foreach (var refNode in node.FromClause.TableReferences)
            {
                if (refNode is NamedTableReference ntr && ntr.SchemaObject?.BaseIdentifier != null)
                {
                    FirstTableName = ntr.SchemaObject.BaseIdentifier.Value;
                    return;
                }
            }
        }
    }
}
