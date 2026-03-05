using System;
using System.Collections.Generic;
using System.Linq;
using SQLsense.Core;
using SQLsense.Infrastructure;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SQLsense.Core.Analysis;

namespace SQLsense.Core.Completion
{
    public class CompletionEngine
    {
        private SnippetManager _snippetManager;

        public CompletionEngine(SnippetManager snippetManager)
        {
            _snippetManager = snippetManager;
        }

        public List<SQLsense.UI.Completion.CompletionItem> GetCompletions(string prefix, string textBeforeCaret, string fullText)
        {
            var results = new List<SQLsense.UI.Completion.CompletionItem>();
            string searchPrefix = prefix?.ToLowerInvariant() ?? string.Empty;

            // We will store a "Match Score" to prioritize StartsWith over Contains
            var matchScores = new Dictionary<SQLsense.UI.Completion.CompletionItem, int>();

            // Strict Context Filtering Flags
            bool allowSnippets = true;
            bool allowTables = true;
            bool allowViews = true;
            bool allowSPs = true;
            bool allowFunctions = true;
            bool allowColumns = true;
            bool allowKeywords = true;

            bool prioritizeTables = false;
            bool prioritizeColumns = false;
            HashSet<string> mentionedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tableAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(textBeforeCaret))
            {
                SqlContextState state = ContextStateAnalyzer.DetermineState(fullText, textBeforeCaret.Length);

                // ALIAS INTERCEPT MASTER SWITCH: Do not suggest anything if the user is literally inventing an alias mid-word
                bool isTypingAliasDefinition = false;
                bool endsWithSpaceOrPunctuation = textBeforeCaret.EndsWith(" ") || textBeforeCaret.EndsWith("\t") || textBeforeCaret.EndsWith("\n") || textBeforeCaret.EndsWith("\r") || textBeforeCaret.EndsWith(",") || textBeforeCaret.EndsWith(".");
                
                if (!endsWithSpaceOrPunctuation && state != SqlContextState.UpdateSetColumns)
                {
                    string trimmedBefore = textBeforeCaret.TrimEnd();
                    int lastSpace = trimmedBefore.LastIndexOfAny(new[] { ' ', '\t', '\n', '\r', ',', '(' });
                    string prevWord = lastSpace >= 0 ? trimmedBefore.Substring(0, lastSpace).TrimEnd() : "none";
                    int prevWordLastSpace = prevWord.LastIndexOfAny(new[] { ' ', '\t', '\n', '\r', ',', '(' });
                    prevWord = prevWordLastSpace >= 0 ? prevWord.Substring(prevWordLastSpace + 1).ToUpperInvariant() : prevWord.ToUpperInvariant();

                    if (prevWord == "AS")
                    {
                        isTypingAliasDefinition = true;
                    }
                    else
                    {
                        string strippedPrevWord = prevWord.Contains(".") ? prevWord.Substring(prevWord.LastIndexOf(".") + 1) : prevWord;
                        strippedPrevWord = strippedPrevWord.Replace("[", "").Replace("]", "");
                        
                        // We need the schema objects to check if prevWord is a table
                        var tmpObjects = DatabaseSchemaProvider.GetCachedObjects();
                        if (System.Linq.Enumerable.Any(tmpObjects, o => (o.IconType == SQLsense.UI.Completion.CompletionIconType.Table || o.IconType == SQLsense.UI.Completion.CompletionIconType.View) 
                                                   && o.Text.Equals(strippedPrevWord, StringComparison.OrdinalIgnoreCase)))
                        {
                            isTypingAliasDefinition = true;
                        }
                    }
                }

                if (isTypingAliasDefinition)
                {
                    return results; // Return empty immediately so the UI gracefully hides while they invent their alias
                }


                if (state == SqlContextState.Unknown)
                {
                    // e.g. typing a value `WHERE id = `
                    allowTables = false;
                    allowViews = false;
                    allowSPs = false;
                    allowSnippets = false;
                    allowKeywords = true;
                    allowFunctions = true;
                    // Note: We leave allowColumns = true because they might do `WHERE t0.Id = t1.Id`
                }
                else if (state == SqlContextState.FromTables || state == SqlContextState.JoinTables || state == SqlContextState.InsertTable || state == SqlContextState.UpdateTable)
                {
                    allowSnippets = false;
                    allowSPs = false;
                    allowColumns = false;
                    allowKeywords = true;
                    prioritizeTables = true;
                    
                    // Suppress tables if we just finished typing one or an alias, so we don't spam tables when they press space
                    if (endsWithSpaceOrPunctuation)
                    {
                        string trimmed = textBeforeCaret.TrimEnd();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            char lastChar = trimmed[trimmed.Length - 1];
                            if (lastChar != ',' && !trimmed.EndsWith("FROM", StringComparison.OrdinalIgnoreCase) && !trimmed.EndsWith("JOIN", StringComparison.OrdinalIgnoreCase) && !trimmed.EndsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && !trimmed.EndsWith("INTO", StringComparison.OrdinalIgnoreCase))
                            {
                                // They finished typing a table or an alias. At this point, they expect structural keywords (WHERE, ON, JOIN, etc.)
                                allowTables = false;
                                prioritizeTables = false;
                            }
                        }
                    }
                }
                else if (state == SqlContextState.SelectColumns || state == SqlContextState.WhereCondition || state == SqlContextState.JoinOnCondition)
                {
                    allowSPs = false;
                    prioritizeColumns = true;

                    if (state == SqlContextState.JoinOnCondition && textBeforeCaret.TrimEnd().EndsWith(" ON", StringComparison.OrdinalIgnoreCase))
                    {
                        var beforeWords = textBeforeCaret.Split(new[] { ' ', '\t', '\n', '\r', '(', ')', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        var joins = GenerateSmartJoins(beforeWords, textBeforeCaret);
                        foreach(var j in joins) {
                            results.Add(j);
                            matchScores[j] = -1; // Top priority
                        }
                    }
                }
                else if (state == SqlContextState.GroupByColumns || state == SqlContextState.OrderByColumns)
                {
                    allowSPs = false;
                    prioritizeColumns = true;

                    string prevWord = state == SqlContextState.GroupByColumns ? "GROUP" : "ORDER";
                    if (textBeforeCaret.TrimEnd().EndsWith(" BY", StringComparison.OrdinalIgnoreCase))
                    {
                        string cols = GenerateGroupBySnippet(fullText);
                        if (!string.IsNullOrEmpty(cols))
                        {
                            string snippetDesc = prevWord == "GROUP" ? "Smart GROUP BY: Insert non-aggregated columns" : "Smart ORDER BY: Insert columns";
                            var snippetItem = new SQLsense.UI.Completion.CompletionItem(cols, snippetDesc, SQLsense.UI.Completion.CompletionIconType.Snippet)
                            {
                                SnippetExpansion = cols
                            };
                            results.Add(snippetItem);
                            matchScores[snippetItem] = -1; // Top priority
                        }
                    }
                }
                else if (state == SqlContextState.UpdateSetColumns)
                {
                    allowSnippets = false;
                    allowSPs = false;
                    allowTables = false;
                    allowViews = false;
                    allowKeywords = false;
                    allowFunctions = false;
                    
                    string trimmed = textBeforeCaret.TrimEnd();
                    bool alreadyHasSet = trimmed.EndsWith(" SET", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("\nSET", StringComparison.OrdinalIgnoreCase);

                    if (!alreadyHasSet)
                    {
                        // Safely inject SET
                        var setItem = new SQLsense.UI.Completion.CompletionItem("SET", "Keyword", SQLsense.UI.Completion.CompletionIconType.Keyword) { SnippetExpansion = "SET " };
                        
                        if (string.IsNullOrEmpty(searchPrefix))
                        {
                            results.Add(setItem);
                            matchScores[setItem] = -2; // Absolute Top Priority
                        }
                        else if ("set".StartsWith(searchPrefix))
                        {
                            results.Add(setItem);
                            matchScores[setItem] = -2; // Strongly Prioritize if spelling matches
                        }
                        else if ("set".Contains(searchPrefix))
                        {
                            results.Add(setItem);
                            matchScores[setItem] = 1;
                        }
                    }
                    
                    // Allow columns to be visible below SET in case the user skips typing SET or is investigating columns
                    prioritizeColumns = true;
                }
            }
            // Extract mentioned tables from the actively localized query text (rather than the full global file text)
                // AST-BASED CONTEXT RESOLUTION
                // We parse the entire document using ScriptDom to find the precise statement hosting the caret.
                if (!string.IsNullOrWhiteSpace(fullText))
                {
                    try
                    {
                        var parser = new TSql160Parser(true);
                        using (var reader = new System.IO.StringReader(fullText))
                        {
                            var fragment = parser.Parse(reader, out var errors);
                            if (fragment != null)
                            {
                                int caretPosition = textBeforeCaret.Length;
                                var activeVisitor = new ActiveStatementVisitor(caretPosition);
                                fragment.Accept(activeVisitor);

                                // The activeVisitor safely extracted only the aliases constrained to our exact statement node
                                foreach (var kvp in activeVisitor.ActiveAliases)
                                {
                                    tableAliases[kvp.Key] = kvp.Value;
                                    
                                    // Populate mentionedTables natively from the confirmed AST values
                                    if (!mentionedTables.Contains(kvp.Value))
                                    {
                                        mentionedTables.Add(kvp.Value);
                                    }
                                }

                                // If AST parsing somehow completely missed (e.g., initial typing with severe errors),
                                // gracefully fallback to a simple word scan on the localized text before caret
                                if (mentionedTables.Count == 0 && textBeforeCaret.Length > 0)
                                {
                                    int lastSemi = textBeforeCaret.LastIndexOf(';');
                                    int lastGo = textBeforeCaret.LastIndexOf("GO\r", StringComparison.OrdinalIgnoreCase);
                                    if (lastGo == -1) lastGo = textBeforeCaret.LastIndexOf("GO\n", StringComparison.OrdinalIgnoreCase);
                                    int cutIdx = Math.Max(lastSemi, lastGo);
                                    string fallbackText = cutIdx >= 0 ? textBeforeCaret.Substring(cutIdx + 1) : textBeforeCaret;

                                    var fallbackWords = fallbackText.Split(new[] { ' ', '\t', '\n', '\r', ',', '(', ')', ';' }, StringSplitOptions.RemoveEmptyEntries);
                                    
                                    // Find the start of the current statement (the last SELECT, UPDATE, DELETE, INSERT)
                                    int statementStartIndex = 0;
                                    for (int i = fallbackWords.Length - 1; i >= 0; i--)
                                    {
                                        var wUpper = fallbackWords[i].ToUpperInvariant();
                                        if (wUpper == "SELECT" || wUpper == "UPDATE" || wUpper == "DELETE" || wUpper == "INSERT")
                                        {
                                            statementStartIndex = i;
                                            break;
                                        }
                                    }

                                    for (int i = statementStartIndex; i < fallbackWords.Length - 1; i++)
                                    {
                                        var wordUpper = fallbackWords[i].ToUpperInvariant();
                                        if (wordUpper == "FROM" || wordUpper == "JOIN" || wordUpper == "UPDATE" || wordUpper == "INTO")
                                        {
                                            string tName = CleanTableName(fallbackWords[i + 1]);
                                            mentionedTables.Add(tName);

                                            // Capture Alias if present in emergency fallback
                                            if (i + 2 < fallbackWords.Length)
                                            {
                                                string potentialAlias = fallbackWords[i + 2];
                                                string potentialAliasUpper = potentialAlias.ToUpperInvariant();
                                                
                                                if (potentialAliasUpper == "AS" && i + 3 < fallbackWords.Length)
                                                {
                                                    tableAliases[fallbackWords[i + 3]] = tName.ToLowerInvariant();
                                                }
                                                // Exclude common structural keywords from being mistakenly parsed as aliases
                                                else if (potentialAliasUpper != "ON" && potentialAliasUpper != "WHERE" && potentialAliasUpper != "JOIN" && potentialAliasUpper != "INNER" && potentialAliasUpper != "LEFT" && potentialAliasUpper != "RIGHT" && potentialAliasUpper != "OUTER" && potentialAliasUpper != "CROSS" && potentialAliasUpper != "GROUP" && potentialAliasUpper != "ORDER" && potentialAliasUpper != "SET" && potentialAliasUpper != "VALUES" && potentialAliasUpper != "OUTPUT")
                                                {
                                                    tableAliases[potentialAlias] = tName.ToLowerInvariant();
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OutputWindowLogger.LogError("ScriptDom Context Parsing Failed", ex);
                    }
                } // This closes if (!string.IsNullOrWhiteSpace(fullText))
            // AST-BASED CONTEXT RESOLUTION END

            // Helper to add item with score
            void AddItem(SQLsense.UI.Completion.CompletionItem item, string searchText)
            {
                if (string.IsNullOrEmpty(searchPrefix))
                {
                    results.Add(item);
                    matchScores[item] = 0;
                    return;
                }

                string lowerText = searchText.ToLowerInvariant();
                string lowerDesc = item.Description?.ToLowerInvariant();
                
                bool isDbObject = item.IconType == SQLsense.UI.Completion.CompletionIconType.Table ||
                                  item.IconType == SQLsense.UI.Completion.CompletionIconType.View ||
                                  item.IconType == SQLsense.UI.Completion.CompletionIconType.StoredProcedure ||
                                  item.IconType == SQLsense.UI.Completion.CompletionIconType.Function ||
                                  item.IconType == SQLsense.UI.Completion.CompletionIconType.Column; // Also allow column descriptions

                if (lowerText.StartsWith(searchPrefix) || (isDbObject && lowerDesc != null && lowerDesc.StartsWith(searchPrefix)))
                {
                    results.Add(item);
                    matchScores[item] = 1; // High priority: Prefix match
                }
                else if (lowerText.Contains(searchPrefix) || (isDbObject && lowerDesc != null && lowerDesc.Contains(searchPrefix)))
                {
                    results.Add(item);
                    matchScores[item] = 2; // Lower priority: Mid-string match
                }
            }

            // Search Snippets first (Higher priority visually, but maybe less if we strictly want columns)
            if (allowSnippets && _snippetManager != null)
            {
                foreach (var snippet in _snippetManager.AllSnippets)
                {
                    AddItem(new SQLsense.UI.Completion.CompletionItem(snippet.Key, snippet.Value, SQLsense.UI.Completion.CompletionIconType.Snippet), snippet.Key);
                }
            }

            // DB Schema Objects (Tables, Views, SPs, etc.)
            var schemaObjects = DatabaseSchemaProvider.GetCachedObjects();
            foreach(var obj in schemaObjects)
            {
                if (!allowTables && obj.IconType == SQLsense.UI.Completion.CompletionIconType.Table) continue;
                if (!allowViews && obj.IconType == SQLsense.UI.Completion.CompletionIconType.View) continue;
                if (!allowSPs && obj.IconType == SQLsense.UI.Completion.CompletionIconType.StoredProcedure) continue;
                if (!allowFunctions && obj.IconType == SQLsense.UI.Completion.CompletionIconType.Function) continue;

                AddItem(obj, obj.Text);
            }

            // Table Aliases
            if (allowTables && tableAliases.Count > 0)
            {
                foreach (var alias in tableAliases)
                {
                    var aliasItem = new SQLsense.UI.Completion.CompletionItem(
                        alias.Key, 
                        "Alias for " + alias.Value, 
                        SQLsense.UI.Completion.CompletionIconType.Table
                    );
                    AddItem(aliasItem, alias.Key);
                }
            }

            // DB Schema Columns
            if (allowColumns)
            {
                var columns = DatabaseSchemaProvider.GetCachedColumns();
                string targetTableConstraint = null;
                string tableOrAlias = null;

                if (!string.IsNullOrEmpty(searchPrefix) && searchPrefix.Contains("."))
                {
                    var parts = searchPrefix.Split(new[] { '.' }, 2);
                    tableOrAlias = parts[0];
                    searchPrefix = parts[1]; // Narrow down search to just the column part

                    if (tableAliases.ContainsKey(tableOrAlias))
                    {
                        targetTableConstraint = tableAliases[tableOrAlias].ToLowerInvariant();
                    }
                    else
                    {
                        targetTableConstraint = tableOrAlias.ToLowerInvariant();
                    }
                }

                // To prevent massive UI lag, only show columns if we have a prefix OR we are in a column-priority context
                if (!string.IsNullOrEmpty(searchPrefix) || prioritizeColumns || searchPrefix.Length >= 1 || targetTableConstraint != null)
                {
                    // De-duplicate column suggestions if they have the exact same name to prevent overwhelming lists
                    var uniqueColumns = new Dictionary<string, SQLsense.UI.Completion.CompletionItem>();

                    foreach (var col in columns)
                    {
                        string colLower = col.Text.ToLowerInvariant();
                        if (string.IsNullOrEmpty(searchPrefix) || colLower.Contains(searchPrefix))
                        {
                            // Check if this column belongs to any of our mentioned tables
                            bool tableMatched = false;
                            
                            if (targetTableConstraint != null)
                            {
                                if (!string.IsNullOrEmpty(col.Description))
                                {
                                    string lowerDesc = col.Description.ToLowerInvariant();
                                    if (lowerDesc.EndsWith("." + targetTableConstraint) || lowerDesc == targetTableConstraint)
                                    {
                                        tableMatched = true;
                                    }
                                }
                            }
                            else if (mentionedTables.Count > 0 && !string.IsNullOrEmpty(col.Description))
                            {
                                string lowerDesc = col.Description.ToLowerInvariant();
                                foreach (var tbl in mentionedTables)
                                {
                                    string tblLower = tbl.ToLowerInvariant();
                                    if (lowerDesc.EndsWith("." + tblLower) || lowerDesc == tblLower)
                                    {
                                        tableMatched = true;
                                        
                                        // If the user didn't explicitly use an alias like 'O.' but matched this column
                                        // via regular typing, check if its parent table ACTUALLY HAS an alias we can prepend.
                                        if (string.IsNullOrEmpty(tableOrAlias))
                                        {
                                            var reversedAliasMatch = tableAliases.FirstOrDefault(x => x.Value == tbl);
                                            if (!string.IsNullOrEmpty(reversedAliasMatch.Key))
                                            {
                                                tableOrAlias = reversedAliasMatch.Key;
                                            }
                                        }
                                        break;
                                    }
                                }
                            }

                        // Only add the column if its parent table was detected in the query
                        if (tableMatched)
                        {
                            if (!uniqueColumns.ContainsKey(col.Text))
                            {
                                // Clone the column so we can optionally tweak it for this specific query context
                                var colClone = new SQLsense.UI.Completion.CompletionItem(col.Text, col.Description, col.IconType);
                                
                                // Prepare its expansion text (e.g. t0.OrderId)
                                if (!string.IsNullOrEmpty(tableOrAlias))
                                {
                                    string aliasUpper = tableOrAlias.ToUpperInvariant();
                                    if (aliasUpper != "SET" && aliasUpper != "VALUES" && aliasUpper != "INTO" && aliasUpper != "OUTPUT")
                                    {
                                        colClone.SnippetExpansion = tableOrAlias + "." + colClone.Text; // Preserve the exact alias case typed by the user
                                    }
                                }

                                uniqueColumns.Add(colClone.Text, colClone);
                                matchScores[colClone] = string.IsNullOrEmpty(searchPrefix) ? 0 : (colLower.StartsWith(searchPrefix) ? 1 : 2);
                            }
                        }
                    }
                }
                
                results.AddRange(uniqueColumns.Values);
                }
            }

            // Search Built-in Functions
            if (allowKeywords) // Assuming functions are allowed in same contexts as keywords
            {
                string[] builtInFunctions = { "SUM", "COUNT", "MIN", "MAX", "AVG", "ISNULL", "GETDATE", "DATEDIFF", "DATEADD", "SUBSTRING", "CHARINDEX", "LEN", "LTRIM", "RTRIM", "UPPER", "LOWER", "ROUND", "CEILING", "FLOOR", "ABS", "NEWID", "CAST", "CONVERT", "COALESCE", "REPLACE" };
                foreach (var func in builtInFunctions)
                {
                    AddItem(new SQLsense.UI.Completion.CompletionItem(func, "Built-in Function", SQLsense.UI.Completion.CompletionIconType.Function), func);
                }
            }

            // Search Keywords
            if (allowKeywords) 
            {
                foreach (var keyword in KeywordManager.AllKeywords)
                {
                    AddItem(new SQLsense.UI.Completion.CompletionItem(keyword.ToUpperInvariant(), "Keyword", SQLsense.UI.Completion.CompletionIconType.Keyword), keyword);
                }
            }

            // Custom Sorting based on Context
            return results
                .OrderBy(x => matchScores.ContainsKey(x) ? matchScores[x] : 2) // Priority 1: Prefix Match, Priority 2: Mid-String
                .ThenBy(x => {
                    if (prioritizeColumns)
                    {
                        if (x.IconType == SQLsense.UI.Completion.CompletionIconType.Column) return -1; // Top priority
                        if (x.IconType == SQLsense.UI.Completion.CompletionIconType.Snippet) return 1;
                        if (x.IconType == SQLsense.UI.Completion.CompletionIconType.Table || x.IconType == SQLsense.UI.Completion.CompletionIconType.View) return 2;
                        return 3;
                    }

                    // Snippets usually map to keywords, so put them near keywords or top depending on context
                    if (x.IconType == SQLsense.UI.Completion.CompletionIconType.Snippet) return 1;

                    if (prioritizeTables)
                    {
                        if (x.IconType == SQLsense.UI.Completion.CompletionIconType.Table || x.IconType == SQLsense.UI.Completion.CompletionIconType.View) return 0;
                        if (x.IconType == SQLsense.UI.Completion.CompletionIconType.Column) return 4; 
                    }

                    // Default ordering
                    if (x.IconType == SQLsense.UI.Completion.CompletionIconType.Table || x.IconType == SQLsense.UI.Completion.CompletionIconType.View) return 2;
                    if (x.IconType == SQLsense.UI.Completion.CompletionIconType.Column) return 3;
                    if (x.IconType == SQLsense.UI.Completion.CompletionIconType.Function || x.IconType == SQLsense.UI.Completion.CompletionIconType.StoredProcedure) return 4;
                    if (x.IconType == SQLsense.UI.Completion.CompletionIconType.Keyword) return 5;
                    
                    return 10;
                })
                .ThenBy(x => x.Text)
                .ToList();
        }

        private string CleanTableName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = name.Replace("[", "").Replace("]", "");
            if (name.Contains(".")) name = name.Substring(name.LastIndexOf(".") + 1);
            return name;
        }

        private string GenerateGroupBySnippet(string fullText)
        {
            try
            {
                int selectIdx = fullText.IndexOf("SELECT ", StringComparison.OrdinalIgnoreCase);
                if (selectIdx == -1) selectIdx = fullText.IndexOf("SELECT\n", StringComparison.OrdinalIgnoreCase);
                if (selectIdx == -1) selectIdx = fullText.IndexOf("SELECT\t", StringComparison.OrdinalIgnoreCase);
                if (selectIdx == -1) selectIdx = fullText.IndexOf("SELECT\r", StringComparison.OrdinalIgnoreCase);
                
                if (selectIdx == -1) return null;
                
                int fromIdx = fullText.IndexOf("FROM ", selectIdx, StringComparison.OrdinalIgnoreCase);
                if (fromIdx == -1) fromIdx = fullText.IndexOf("FROM\n", selectIdx, StringComparison.OrdinalIgnoreCase);
                if (fromIdx == -1) return null;
                
                string selectBody = fullText.Substring(selectIdx + 6, fromIdx - (selectIdx + 6)).Trim();
                
                // Parse select body by comma, respecting depth of parenthesis
                var parts = new List<string>();
                int depth = 0;
                int start = 0;
                for(int i = 0; i < selectBody.Length; i++)
                {
                    if (selectBody[i] == '(') depth++;
                    else if (selectBody[i] == ')') depth--;
                    else if (selectBody[i] == ',' && depth == 0)
                    {
                        parts.Add(selectBody.Substring(start, i - start));
                        start = i + 1;
                    }
                }
                parts.Add(selectBody.Substring(start));
                
                var nonAggCols = new List<string>();
                string[] aggFuncs = { "SUM(", "COUNT(", "MIN(", "MAX(", "AVG(" };
                
                foreach(var part in parts)
                {
                    string p = part.Trim();
                    if (string.IsNullOrEmpty(p)) continue;
                    
                    if (p.StartsWith("TOP ", StringComparison.OrdinalIgnoreCase))
                    {
                        // Remove TOP X
                        string[] tokens = p.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length >= 3)
                        {
                            p = string.Join(" ", tokens.Skip(2));
                        }
                    }
                    if (p.StartsWith("DISTINCT ", StringComparison.OrdinalIgnoreCase))
                    {
                        p = p.Substring(9).Trim();
                    }
                    
                    bool isAgg = false;
                    foreach(var agg in aggFuncs)
                    {
                        if (p.IndexOf(agg, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isAgg = true;
                            break;
                        }
                    }
                    
                    if (!isAgg)
                    {
                        // Remove AS Alias
                        int asIdx = p.LastIndexOf(" AS ", StringComparison.OrdinalIgnoreCase);
                        if (asIdx > 0 && asIdx > p.LastIndexOf(')'))
                        {
                            p = p.Substring(0, asIdx).Trim();
                        }
                        else
                        {
                            // Strip space-based aliases like `Table.Col Header`
                            int spaceIdx = p.LastIndexOf(' ');
                            if (spaceIdx > 0 && spaceIdx > p.LastIndexOf(')'))
                            {
                                string[] tokens = p.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (tokens.Length == 2 && p.IndexOf('(') == -1)
                                {
                                    p = tokens[0];
                                }
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(p) && p != "*" && !p.EndsWith(".*"))
                        {
                            nonAggCols.Add(p);
                        }
                    }
                }
                
                if (nonAggCols.Count > 0)
                {
                    return string.Join(", ", nonAggCols);
                }
            }
            catch (Exception ex)
            {
                SQLsense.Infrastructure.OutputWindowLogger.LogError("Failed to parse GroupBy", ex);
            }
            return null;
        }

        private List<string> GetColumnsForTable(string tableName)
        {
            if (string.IsNullOrEmpty(tableName)) return new List<string>();
            var allCols = DatabaseSchemaProvider.GetCachedColumns();
            
            return allCols
                .Where(c => !string.IsNullOrEmpty(c.Description) && 
                           (c.Description.EndsWith("." + tableName, StringComparison.OrdinalIgnoreCase) || 
                            c.Description.Equals(tableName, StringComparison.OrdinalIgnoreCase)))
                .Select(c => c.Text)
                .ToList();
        }

        private List<(string Col1, string Col2)> FindJoinConditions(string t1Name, List<string> t1Cols, string t2Name, List<string> t2Cols)
        {
            var joins = new List<(string Col1, string Col2)>();
            
            var t1Lower = t1Cols.Select(x => x.ToLowerInvariant()).ToList();
            var t2Lower = t2Cols.Select(x => x.ToLowerInvariant()).ToList();
            
            // 1. Exact Name Matches (e.g. both have CustomerId)
            var exactMatches = t1Cols.Where(c1 => t2Cols.Any(c2 => c1.Equals(c2, StringComparison.OrdinalIgnoreCase))).ToList();
            foreach(var match in exactMatches)
            {
                if (match.EndsWith("Id", StringComparison.OrdinalIgnoreCase) || match.EndsWith("ID", StringComparison.OrdinalIgnoreCase) || match.EndsWith("_id", StringComparison.OrdinalIgnoreCase))
                {
                    joins.Add((match, match));
                }
            }
            
            // 2. t1 has Id, t2 has Table1Id
            string id = "id";
            string t1Id1 = t1Name.ToLowerInvariant() + "id";
            string t1Id2 = t1Name.ToLowerInvariant() + "_id";
            string t2Id1 = t2Name.ToLowerInvariant() + "id";
            string t2Id2 = t2Name.ToLowerInvariant() + "_id";
            
            if (t1Lower.Contains(id))
            {
                var col1 = t1Cols[t1Lower.IndexOf(id)];
                if (t2Lower.Contains(t1Id1)) joins.Add((col1, t2Cols[t2Lower.IndexOf(t1Id1)]));
                else if (t2Lower.Contains(t1Id2)) joins.Add((col1, t2Cols[t2Lower.IndexOf(t1Id2)]));
            }
            
            // 3. t2 has Id, t1 has Table2Id
            if (t2Lower.Contains(id))
            {
                var col2 = t2Cols[t2Lower.IndexOf(id)];
                if (t1Lower.Contains(t2Id1)) joins.Add((t1Cols[t1Lower.IndexOf(t2Id1)], col2));
                else if (t1Lower.Contains(t2Id2)) joins.Add((t1Cols[t1Lower.IndexOf(t2Id2)], col2));
            }
            
            return joins.Distinct().ToList();
        }

        private List<SQLsense.UI.Completion.CompletionItem> GenerateSmartJoins(string[] beforeWords, string textBeforeCaret)
        {
            var results = new List<SQLsense.UI.Completion.CompletionItem>();
            string table1 = null;
            string alias1 = null;
            string table2 = null;
            string alias2 = null;
            
            try
            {
                // Regex to find: FROM/JOIN [schema].[table] AS alias
                // Differentiates between [table with spaces] and normal_table
                var regex = new System.Text.RegularExpressions.Regex(
                    @"(?i)(?:FROM|JOIN)\s+(?:\[?[\w]+\]?\.)?(?:\[([\w\s]+)\]|([\w]+))(?:\s+(?:AS\s+)?(?!(?:JOIN|INNER|LEFT|RIGHT|OUTER|CROSS|FULL|ON|WHERE|GROUP|ORDER|HAVING|SELECT)\b)([a-zA-Z0-9_]+))?", 
                    System.Text.RegularExpressions.RegexOptions.RightToLeft);

                var matches = regex.Matches(textBeforeCaret);
                
                if (matches.Count >= 1)
                {
                    // The first match from RightToLeft is the LAST join
                    table2 = string.IsNullOrEmpty(matches[0].Groups[1].Value) ? matches[0].Groups[2].Value.Trim() : matches[0].Groups[1].Value.Trim();
                    alias2 = matches[0].Groups[3].Success ? matches[0].Groups[3].Value.Trim() : null;
                }
                
                if (matches.Count >= 2)
                {
                    // The second match from RightToLeft is the previous FROM/JOIN
                    table1 = string.IsNullOrEmpty(matches[1].Groups[1].Value) ? matches[1].Groups[2].Value.Trim() : matches[1].Groups[1].Value.Trim();
                    alias1 = matches[1].Groups[3].Success ? matches[1].Groups[3].Value.Trim() : null;
                }
            }
            catch (Exception ex)
            {
                 OutputWindowLogger.LogError("Regex join parsing failed", ex);
            }
            
            table1 = CleanTableName(table1);
            table2 = CleanTableName(table2);
            
            if (table1 != null && table2 != null)
            {
                var t1Cols = GetColumnsForTable(table1);
                var t2Cols = GetColumnsForTable(table2);

                var matches = FindJoinConditions(table1, t1Cols, table2, t2Cols);
                foreach (var match in matches)
                {
                    string prefix1 = string.IsNullOrEmpty(alias1) ? table1 : alias1;
                    string prefix2 = string.IsNullOrEmpty(alias2) ? table2 : alias2;
                    string snip = $"{prefix1}.{match.Col1} = {prefix2}.{match.Col2}";
                    results.Add(new SQLsense.UI.Completion.CompletionItem(snip, "Smart JOIN", SQLsense.UI.Completion.CompletionIconType.Snippet));
                }
            }
            return results;
        }

        private string ExtractActiveStatement(string fullText, string textBeforeCaret)
        {
            if (string.IsNullOrWhiteSpace(textBeforeCaret)) return fullText;

            try 
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(fullText, @"\S+");
                var beforeMatches = System.Text.RegularExpressions.Regex.Matches(textBeforeCaret, @"\S+");

                if (beforeMatches.Count == 0) return fullText;

                int activeIndex = beforeMatches.Count - 1;
                int startIndex = 0;
                string[] statementRoots = { "SELECT", "UPDATE", "DELETE", "INSERT", "MERGE", "TRUNCATE", "GO", ";" };
                
                // Scan backwards for DML roots
                for (int i = activeIndex; i >= 0; i--)
                {
                    string cleanToken = matches[i].Value.ToUpperInvariant().Trim(new char[] { '(', ')', ';' });
                    if (statementRoots.Contains(cleanToken) || matches[i].Value.Contains(";"))
                    {
                        startIndex = i;
                        if (matches[i].Value.Contains(";") && cleanToken != "GO") {
                            startIndex = i + 1; // Start strictly after the semicolon
                        }
                        
                        // Heuristic guard: ignore nested SELECTs inside parentheses by checking leading parens
                        int openParens = 0, closeParens = 0;
                        for(int p = i; p <= activeIndex; p++) {
                            openParens += matches[p].Value.Count(c => c == '(');
                            closeParens += matches[p].Value.Count(c => c == ')');
                        }
                        if (openParens > closeParens) {
                            // This was a subquery, keep looking backwards
                            continue;
                        }

                        break; // Valid boundary found
                    }
                }

                // Scan forwards for the NEXT DML root
                int endIndex = matches.Count - 1;
                for (int i = activeIndex + 1; i < matches.Count; i++)
                {
                    string cleanToken = matches[i].Value.ToUpperInvariant().Trim(new char[] { '(', ')', ';' });
                    if (statementRoots.Contains(cleanToken) || matches[i].Value.Contains(";"))
                    {
                        int openParens = 0, closeParens = 0;
                        for(int p = activeIndex; p <= i; p++) {
                            openParens += matches[p].Value.Count(c => c == '(');
                            closeParens += matches[p].Value.Count(c => c == ')');
                        }
                        if (closeParens > openParens) {
                            continue; // Closing a subquery
                        }

                        endIndex = matches[i].Value.Contains(";") ? i : i - 1;
                        break;
                    }
                }

                if (startIndex > endIndex) return fullText; // Fail-safe
                if (startIndex < 0) startIndex = 0;
                if (endIndex >= matches.Count) endIndex = matches.Count - 1;
                
                var isolatedTokens = new System.Collections.Generic.List<string>();
                for(int i = startIndex; i <= endIndex; i++) {
                    isolatedTokens.Add(matches[i].Value);
                }
                
                return string.Join(" ", isolatedTokens);
            }
            catch(Exception ex)
            {
                SQLsense.Infrastructure.OutputWindowLogger.LogError("Statement context isolation failed", ex);
                return fullText; // If anything goes wrong, default to the full text
            }
        }
    }
}
