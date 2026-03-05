using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SQLsense.Core.Analysis
{
    /// <summary>
    /// Traverses a ScriptDom AST to locate the specific TSqlStatement that the user's caret is currently inside.
    /// Once the active statement is found, it extracts all formal Table Aliases defined strictly within that statement.
    /// This prevents "Alias Bleeding" between separate queries in the same editor window.
    /// </summary>
    internal class ActiveStatementVisitor : TSqlFragmentVisitor
    {
        private readonly int _caretPosition;
        
        public TSqlStatement ActiveStatement { get; private set; }
        public Dictionary<string, string> ActiveAliases { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ActiveStatementVisitor(int caretPosition)
        {
            _caretPosition = caretPosition;
        }

        public override void ExplicitVisit(TSqlBatch node)
        {
            // We iterate manually to find the matching statement so we don't bother traversing irrelevant statements
            foreach (var stmt in node.Statements)
            {
                if (IsCaretInsideStatement(stmt))
                {
                    ActiveStatement = stmt;
                    
                    // Now that we have the exact statement, we spawn a secondary visitor JUST for this node
                    // to extract its strict aliases, guaranteeing no leakage.
                    var aliasVisitor = new AliasExtractionVisitor(ActiveAliases);
                    stmt.Accept(aliasVisitor);
                    break;
                }
            }

            base.ExplicitVisit(node);
        }

        private bool IsCaretInsideStatement(TSqlStatement stmt)
        {
            if (stmt.ScriptTokenStream == null || stmt.FirstTokenIndex < 0 || stmt.LastTokenIndex >= stmt.ScriptTokenStream.Count)
            {
                return false;
            }

            int startOffset = stmt.ScriptTokenStream[stmt.FirstTokenIndex].Offset;
            int lastTokenOffset = stmt.ScriptTokenStream[stmt.LastTokenIndex].Offset;
            int lastTokenLength = stmt.ScriptTokenStream[stmt.LastTokenIndex].Text.Length;
            
            int endOffset = lastTokenOffset + lastTokenLength;

            // We add a +2 buffer because when actively typing (e.g. `UPDATE Order SET `), 
            // the caret is technically 1 or 2 spaces past the boundary of the strictly parsed Tree.
            return _caretPosition >= startOffset && _caretPosition <= (endOffset + 2);
        }
    }

    /// <summary>
    /// A private internal visitor used strictly to walk down from an already-isolated TSqlStatement
    /// to harvest all valid NamedTableReferences and their Aliases.
    /// </summary>
    internal class AliasExtractionVisitor : TSqlFragmentVisitor
    {
        private readonly Dictionary<string, string> _aliases;

        public AliasExtractionVisitor(Dictionary<string, string> aliases)
        {
            _aliases = aliases;
        }

        public override void ExplicitVisit(NamedTableReference node)
        {
            if (node.SchemaObject != null && node.SchemaObject.BaseIdentifier != null && node.Alias != null)
            {
                string tableName = node.SchemaObject.BaseIdentifier.Value;
                string aliasName = node.Alias.Value;
                
                // Prevent accidental overrides if the same alias is somehow reused
                if (!_aliases.ContainsKey(aliasName))
                {
                    _aliases[aliasName] = tableName;
                }
            }
            base.ExplicitVisit(node);
        }
    }
}
