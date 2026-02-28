using System;
using System.Collections.Generic;
using System.Linq;
using SQLsense.Core;
using SQLsense.Infrastructure;

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
                // Split all words before the caret
                var beforeWords = textBeforeCaret.Split(new[] { ' ', '\t', '\n', '\r', '(', ')', ',', '=', '<', '>', '+' }, StringSplitOptions.RemoveEmptyEntries);
                
                if (beforeWords.Length > 0)
                {
                    // Find the last actual keyword typed
                    for (int i = beforeWords.Length - 1; i >= 0; i--)
                    {
                        string w = beforeWords[i].ToUpperInvariant();
                        
                        // If the currently typed word is literally just this keyword, skip checking it against itself 
                        // unless prefix is empty (meaning they typed the keyword AND a space)
                        if (i == beforeWords.Length - 1 && !string.IsNullOrEmpty(searchPrefix))
                        {
                            continue;
                        }

                        if (w == "FROM" || w == "JOIN" || w == "INTO" || w == "UPDATE")
                        {
                            allowSnippets = false;
                            allowSPs = false;
                            allowColumns = false;
                            allowKeywords = false;
                            prioritizeTables = true;
                            break;
                        }
                        if (w == "EXEC" || w == "EXECUTE")
                        {
                            allowSnippets = false;
                            allowTables = false;
                            allowViews = false;
                            allowColumns = false;
                            allowKeywords = false;
                            break;
                        }
                        if (w == "SELECT" || w == "WHERE" || w == "ON" || w == "SET" || w == "AND" || w == "OR" || w == "BY" || w == "HAVING")
                        {
                            allowSPs = false;
                            prioritizeColumns = true;
                            if (w == "ON" || (i == beforeWords.Length - 1 && w == "ON"))
                            {
                                var joins = GenerateSmartJoins(beforeWords, textBeforeCaret);
                                foreach(var j in joins) {
                                    results.Add(j);
                                    matchScores[j] = -1; // Top priority
                                }
                            }
                            else if (w == "BY" || (i == beforeWords.Length - 1 && w == "BY"))
                            {
                                string prevWord = (i > 0) ? beforeWords[i - 1].ToUpperInvariant() : "";
                                // if 'BY' is the very last word before caret, it will be at length - 1, and its prefix is at length - 2
                                if (i == beforeWords.Length - 1 && textBeforeCaret.TrimEnd().EndsWith(" BY", StringComparison.OrdinalIgnoreCase))
                                {
                                    prevWord = (beforeWords.Length > 1) ? beforeWords[beforeWords.Length - 2].ToUpperInvariant() : "";
                                }

                                if (prevWord == "GROUP" || prevWord == "ORDER")
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
                            break;
                        }
                    }
                }

                // Extract mentioned tables from the FULL text to support Selects written before Froms
                if (!string.IsNullOrWhiteSpace(fullText))
                {
                    var fullWords = fullText.Split(new[] { ' ', '\t', '\n', '\r', ',', '(', ')', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < fullWords.Length - 1; i++)
                    {
                        var wordUpper = fullWords[i].ToUpperInvariant();
                        if (wordUpper == "FROM" || wordUpper == "JOIN" || wordUpper == "UPDATE" || wordUpper == "INTO")
                        {
                            string tableName = fullWords[i + 1].ToLowerInvariant().Replace("[", "").Replace("]", "");
                            // Sometimes there is a schema dbo.Table
                            if (tableName.Contains("."))
                            {
                                tableName = tableName.Substring(tableName.LastIndexOf(".") + 1);
                            }
                            mentionedTables.Add(tableName);

                            // Capture Alias if present
                            if (i + 2 < fullWords.Length)
                            {
                                string potentialAlias = fullWords[i + 2];
                                string potentialAliasUpper = potentialAlias.ToUpperInvariant();
                                
                                if (potentialAliasUpper == "AS" && i + 3 < fullWords.Length)
                                {
                                    tableAliases[fullWords[i + 3]] = tableName.ToLowerInvariant();
                                }
                                else if (potentialAliasUpper != "ON" && potentialAliasUpper != "WHERE" && potentialAliasUpper != "JOIN" && potentialAliasUpper != "INNER" && potentialAliasUpper != "LEFT" && potentialAliasUpper != "RIGHT" && potentialAliasUpper != "OUTER" && potentialAliasUpper != "CROSS" && potentialAliasUpper != "GROUP" && potentialAliasUpper != "ORDER")
                                {
                                    tableAliases[potentialAlias] = tableName.ToLowerInvariant();
                                }
                            }
                        }
                    }
                }
            }

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
                                  item.IconType == SQLsense.UI.Completion.CompletionIconType.Function;

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
            // Do not show aliases if the user is currently typing an alias definition (i.e. right after an existing Table name, or right after "AS")
            if (allowTables && tableAliases.Count > 0)
            {
                bool isTypingAliasDefinition = false;
                if (!string.IsNullOrWhiteSpace(textBeforeCaret))
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
                        // Check if prevWord is one of the schema objects (tables/views) meaning they are assigning an alias right now
                        // We compare against the stripped name in case they typed dbo.Table or [dbo].[Table]
                        string strippedPrevWord = prevWord.Contains(".") ? prevWord.Substring(prevWord.LastIndexOf(".") + 1) : prevWord;
                        strippedPrevWord = strippedPrevWord.Replace("[", "").Replace("]", "");

                        if (schemaObjects.Any(o => (o.IconType == SQLsense.UI.Completion.CompletionIconType.Table || o.IconType == SQLsense.UI.Completion.CompletionIconType.View) 
                                                   && o.Text.Equals(strippedPrevWord, StringComparison.OrdinalIgnoreCase)))
                        {
                            isTypingAliasDefinition = true;
                        }
                    }
                }

                if (!isTypingAliasDefinition)
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
                                    if (lowerDesc.EndsWith("." + tbl) || lowerDesc == tbl)
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
                                    colClone.SnippetExpansion = tableOrAlias + "." + colClone.Text; // Preserve the exact alias case typed by the user
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
            if (allowKeywords && results.Count < 50) // prevent massive keyword dumps if there are lots of matches
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
    }
}
