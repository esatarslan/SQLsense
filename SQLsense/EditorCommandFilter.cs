using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SQLsense.Core;
using SQLsense.Core.Analysis;
using SQLsense.Infrastructure;

namespace SQLsense
{
    internal class EditorCommandFilter : IOleCommandTarget
    {
        private readonly IWpfTextView _textView;
        private readonly ISqlFormatter _formatter;
        private readonly SnippetManager _snippetManager;
        private readonly SqlAnalyzer _analyzer;
        private readonly System.Timers.Timer _analysisTimer;
        private UI.Completion.CompletionWindow _completionWindow;
        private Core.Completion.CompletionEngine _completionEngine;
        internal IOleCommandTarget _nextCommandTarget;

        public EditorCommandFilter(IWpfTextView textView)
        {
            _textView = textView;
            _formatter = new SqlFormatter();
            _snippetManager = new SnippetManager();
            _analyzer = new SqlAnalyzer();
            _completionEngine = new Core.Completion.CompletionEngine(_snippetManager);

            _analysisTimer = new System.Timers.Timer(1000); // Analyze after 1 second of inactivity
            _analysisTimer.AutoReset = false;
            _analysisTimer.Elapsed += (s, e) => TriggerAnalysis();

            // Auto-hide the completion window when the editor loses focus or if the user clicks/moves caret
            _textView.LostAggregateFocus += (s, e) => _completionWindow?.Hide();
            _textView.VisualElement.PreviewMouseDown += (s, e) => _completionWindow?.Hide();
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _nextCommandTarget.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            bool didExecuteNatively = false;
            int hresult = VSConstants.S_OK;

            try
            {
                char typedChar = char.MinValue;

                if (pguidCmdGroup == VSConstants.VSStd2K)
                {
                    var cmdId = (VSConstants.VSStd2KCmdID)nCmdID;

                    // 1. Intercept Navigation if Window is open
                    if (_completionWindow != null && _completionWindow.IsVisible)
                    {
                        if (cmdId == VSConstants.VSStd2KCmdID.DOWN) { _completionWindow.MoveDown(); return VSConstants.S_OK; }
                        if (cmdId == VSConstants.VSStd2KCmdID.UP) { _completionWindow.MoveUp(); return VSConstants.S_OK; }
                        if (cmdId == VSConstants.VSStd2KCmdID.PAGEDN) { _completionWindow.MovePageDown(); return VSConstants.S_OK; }
                        if (cmdId == VSConstants.VSStd2KCmdID.PAGEUP) { _completionWindow.MovePageUp(); return VSConstants.S_OK; }
                        
                        if (cmdId == VSConstants.VSStd2KCmdID.CANCEL) 
                        { 
                            _completionWindow.Hide(); 
                            return VSConstants.S_OK; 
                        }
                        if (cmdId == VSConstants.VSStd2KCmdID.LEFT || 
                            cmdId == VSConstants.VSStd2KCmdID.RIGHT || 
                            cmdId == VSConstants.VSStd2KCmdID.HOME || 
                            cmdId == VSConstants.VSStd2KCmdID.END)
                        {
                            _completionWindow.Hide();
                        }
                        
                        if (cmdId == VSConstants.VSStd2KCmdID.RETURN || cmdId == VSConstants.VSStd2KCmdID.TAB)
                        {
                            if (_completionWindow.HasSelection)
                            {
                                _completionWindow.CommitSelection();
                                return VSConstants.S_OK; // Swallow native insert
                            }
                            else
                            {
                                _completionWindow.Hide();
                            }
                        }
                    }

                    // 2. Handle Ctrl+Space and native triggers
                    if (cmdId == VSConstants.VSStd2KCmdID.COMPLETEWORD || 
                        cmdId == VSConstants.VSStd2KCmdID.AUTOCOMPLETE || 
                        cmdId == VSConstants.VSStd2KCmdID.SHOWMEMBERLIST)
                    {
                        TriggerCompletion();
                        return VSConstants.S_OK; // Swallow native box completely
                    }

                    if (cmdId == VSConstants.VSStd2KCmdID.TYPECHAR)
                    {
                        if (pvaIn != IntPtr.Zero)
                        {
                            typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
                            if (typedChar == ' ' || typedChar == '\t')
                            {
                                if (TryExpandSnippet()) return VSConstants.S_OK;
                            }
                        }
                    }
                    else if (cmdId == VSConstants.VSStd2KCmdID.RETURN || cmdId == VSConstants.VSStd2KCmdID.TAB || cmdId == VSConstants.VSStd2KCmdID.BACKTAB)
                    {
                        if (TryExpandSnippet()) return VSConstants.S_OK;
                        if (cmdId == VSConstants.VSStd2KCmdID.TAB && TryExpandWildcard()) return VSConstants.S_OK;
                    }
                } // END: if (pguidCmdGroup == VSConstants.VSStd2K)

                // Let the character be typed natively into the editor
                hresult = _nextCommandTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                didExecuteNatively = true;

                // Post-typing logic
                if (hresult == VSConstants.S_OK && pguidCmdGroup == VSConstants.VSStd2K)
                {
                    var cmdId = (VSConstants.VSStd2KCmdID)nCmdID;
                    
                    if (cmdId == VSConstants.VSStd2KCmdID.TYPECHAR || cmdId == VSConstants.VSStd2KCmdID.BACKSPACE)
                    {
                        bool isWindowVisible = _completionWindow != null && _completionWindow.IsVisible;
                        bool isTypeCharTrigger = false;
                        
                        if (cmdId == VSConstants.VSStd2KCmdID.TYPECHAR && pvaIn != IntPtr.Zero)
                        {
                            char tChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
                            isTypeCharTrigger = (char.IsLetterOrDigit(tChar) || tChar == '@' || tChar == ' ' || tChar == ',' || tChar == '.');
                            typedChar = tChar; // Capture for FormatKeywordsAsync later
                        }

                        bool isBackspaceWithVisibleWindow = cmdId == VSConstants.VSStd2KCmdID.BACKSPACE && isWindowVisible;

                        if (isTypeCharTrigger || isBackspaceWithVisibleWindow)
                        {
                            bool showedCustomUI = TriggerCompletion();
                            if (showedCustomUI)
                            {
                                Guid vsStd2KGroup = VSConstants.VSStd2K;
                                _nextCommandTarget.Exec(ref vsStd2KGroup, (uint)VSConstants.VSStd2KCmdID.CANCEL, 0, IntPtr.Zero, IntPtr.Zero);
                            }
                        }
                        else
                        {
                            _completionWindow?.Hide();
                        }
                    }

                    if (cmdId == VSConstants.VSStd2KCmdID.TYPECHAR || cmdId == VSConstants.VSStd2KCmdID.RETURN || cmdId == VSConstants.VSStd2KCmdID.TAB)
                    {
                        if (typedChar == ' ' || typedChar == '\t' || cmdId == VSConstants.VSStd2KCmdID.RETURN || cmdId == VSConstants.VSStd2KCmdID.TAB)
                        {
                            _ = FormatKeywordsAsync();
                            ResetAnalysisTimer();
                        }
                    }
                }

                // Post-paste trigger: detect Paste and trigger IntelliSense for ALTER context
                if (hresult == VSConstants.S_OK && pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
                {
                    uint pasteId = (uint)VSConstants.VSStd97CmdID.Paste;
                    if (nCmdID == pasteId)
                    {
                        bool showedCustomUI = TriggerCompletion();
                        if (showedCustomUI)
                        {
                            Guid vsStd2KGroup = VSConstants.VSStd2K;
                            _nextCommandTarget.Exec(ref vsStd2KGroup, (uint)VSConstants.VSStd2KCmdID.CANCEL, 0, IntPtr.Zero, IntPtr.Zero);
                        }
                    }
                }

                return hresult;
            }
            catch (Exception ex)
            {
                OutputWindowLogger.LogError($"Exec filter crash on Cmd: {nCmdID} - {ex.Message}", ex);
                if (!didExecuteNatively)
                {
                    // Fallback securely so the user doesn't lose keystrokes (like Backspace / Left Arrow)
                    return _nextCommandTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                }
                return hresult;
            }
        }

        private bool TriggerCompletion()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var caretPosition = _textView.Caret.Position.BufferPosition;
                string fullText = _textView.TextBuffer.CurrentSnapshot.GetText();
                string textBeforeCaret = fullText.Substring(0, caretPosition.Position);
                
                // Find the active text being typed by looking back from caret to the first non-valid identifier char
                int wordStart = caretPosition.Position - 1;
                while (wordStart >= 0)
                {
                    char c = fullText[wordStart];
                    if (!char.IsLetterOrDigit(c) && c != '_' && c != '@' && c != '#' && c != '.')
                    {
                        break;
                    }
                    wordStart--;
                }
                
                string currentWord = fullText.Substring(wordStart + 1, caretPosition.Position - (wordStart + 1));

                // 1) Literal or Comment Safety Check
                bool inString = false;
                bool inLineComment = false;
                bool inBlockComment = false;

                for (int i = 0; i < textBeforeCaret.Length; i++)
                {
                    char c = textBeforeCaret[i];
                    char nextC = i + 1 < textBeforeCaret.Length ? textBeforeCaret[i + 1] : '\0';

                    if (!inString && !inLineComment && !inBlockComment)
                    {
                        if (c == '\'') inString = true;
                        else if (c == '-' && nextC == '-') { inLineComment = true; i++; }
                        else if (c == '/' && nextC == '*') { inBlockComment = true; i++; }
                    }
                    else if (inString)
                    {
                        if (c == '\'')
                        {
                            if (nextC == '\'') i++; // escaped quote
                            else inString = false;
                        }
                    }
                    else if (inLineComment)
                    {
                        if (c == '\n') inLineComment = false;
                    }
                    else if (inBlockComment)
                    {
                        if (c == '*' && nextC == '/') { inBlockComment = false; i++; }
                    }
                }

                if (inString || inLineComment || inBlockComment)
                {
                    _completionWindow?.Hide();
                    return false;
                }

                // 2) Pure Number Check (Don't trigger UI on literals like '123' or '1.5')
                if (!string.IsNullOrWhiteSpace(currentWord))
                {
                    bool isAllDigits = true;
                    int dotCount = 0;
                    foreach(var ch in currentWord)
                    {
                        if (ch == '.') dotCount++;
                        else if (!char.IsDigit(ch)) { isAllDigits = false; break; }
                    }
                    
                    if (isAllDigits && dotCount <= 1)
                    {
                        _completionWindow?.Hide();
                        return false;
                    }
                }

                // Trigger background schema refresh implicitly on typing
                _ = SQLsense.Core.Completion.DatabaseSchemaProvider.TriggerRefreshInBackgroundAsync();

                // If they haven't typed a word yet (e.g., just hit space), we should ONLY show the window 
                // if the AST explicitly commands a structured injection (like expecting SET or Columns)
                if (string.IsNullOrWhiteSpace(currentWord))
                {
                    SqlContextState astState = ContextStateAnalyzer.DetermineState(fullText, caretPosition.Position);
                    
                    bool astDemandsUI = astState == SqlContextState.UpdateSetColumns || 
                                        astState == SqlContextState.SelectColumns || 
                                        astState == SqlContextState.JoinOnCondition ||
                                        astState == SqlContextState.WhereCondition ||
                                        astState == SqlContextState.GroupByColumns ||
                                        astState == SqlContextState.OrderByColumns ||
                                        astState == SqlContextState.UpdateTable ||
                                        astState == SqlContextState.InsertTable ||
                                        astState == SqlContextState.FromTables ||
                                        astState == SqlContextState.JoinTables ||
                                        astState == SqlContextState.ExecProcedure ||
                                        astState == SqlContextState.AlterProcedure ||
                                        astState == SqlContextState.AlterView ||
                                        astState == SqlContextState.AlterFunction;

                    if (!astDemandsUI)
                    {
                        _completionWindow?.Hide();
                        return false;
                    }
                }

                var items = _completionEngine.GetCompletions(currentWord, textBeforeCaret, fullText);
                SQLsense.Infrastructure.OutputWindowLogger.Log($"GetCompletions returned {items.Count} items.");
                if (items.Count == 0)
                {
                    _completionWindow?.Hide();
                    return false;
                }

                if (_completionWindow == null)
                {
                    _completionWindow = new UI.Completion.CompletionWindow();
                    _completionWindow.ItemSelected += OnCompletionItemSelected;
                    _completionWindow.ClosedByUser += (s, e) => _completionWindow.Hide();
                    _completionWindow.Deactivated += (s, e) => _completionWindow.Hide(); // Hide when losing focus
                }
                // We want to auto-select the first item only if there's a strict prefix match
                bool hasStrictPrefixMatch = !string.IsNullOrEmpty(currentWord) && 
                                            items.Count > 0 && 
                                            items[0].Text.StartsWith(currentWord, StringComparison.OrdinalIgnoreCase);
                                            
                _completionWindow.SetItems(items, hasStrictPrefixMatch);

                // Auto-select exact match (e.g., after paste in ALTER context)
                if (!string.IsNullOrEmpty(currentWord))
                {
                    _completionWindow.SelectExactMatch(currentWord);
                }

                // Calculate screen position natively via VS Text View
                var textViewLine = _textView.TextViewLines.GetTextViewLineContainingBufferPosition(caretPosition);
                var bounds = textViewLine.GetCharacterBounds(caretPosition);
                
                // Subtract the viewport scroll offsets to get the exact relative X/Y pixel within the VisualElement window
                double viewX = bounds.Left - _textView.ViewportLeft;
                double viewY = bounds.Bottom - _textView.ViewportTop;
                
                // Translate the logical view pixels to absolute monitor screen pixels
                var screenTopLeft = _textView.VisualElement.PointToScreen(new System.Windows.Point(viewX, viewY));
                
                _completionWindow.Left = screenTopLeft.X;
                _completionWindow.Top = screenTopLeft.Y;

                if (!_completionWindow.IsVisible)
                {
                    _completionWindow.Show();
                }
                return true;
            }
            catch (Exception ex)
            {
                OutputWindowLogger.LogError("Failed to show custom completion window", ex);
                return false;
            }
        }

        private void OnCompletionItemSelected(object sender, UI.Completion.CompletionItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var caretPosition = _textView.Caret.Position.BufferPosition;
                var line = caretPosition.GetContainingLine();
                int caretOffsetInLine = caretPosition.Position - line.Start.Position;
                string textBeforeCaret = line.GetText().Substring(0, caretOffsetInLine);
                
                int lastSpaceIndex = textBeforeCaret.LastIndexOfAny(new char[] { ' ', '\t', '\n', '\r', '(', ')', ',', '=', '<', '>', '+' });
                int wordStart = lastSpaceIndex == -1 ? line.Start.Position : line.Start.Position + lastSpaceIndex + 1;
                int currentWordLength = caretPosition.Position - wordStart;

                string expansion = item.Text;
                
                // Determine the previous keyword roughly (e.g., if they typed "EXEC sp_" or "INSERT INTO tbl_")
                string prevWord = "";
                int relativeWordStart = wordStart - line.Start.Position;
                if (relativeWordStart > 0)
                {
                    string textBeforeCurrentWord = line.GetText().Substring(0, relativeWordStart).TrimEnd();
                    int spaceBeforeThat = textBeforeCurrentWord.LastIndexOfAny(new char[] { ' ', '\t', '\n', '\r', '(', ')', ',', '=', '<', '>', '+' });
                    prevWord = spaceBeforeThat == -1 ? textBeforeCurrentWord : textBeforeCurrentWord.Substring(spaceBeforeThat + 1);
                    prevWord = prevWord.ToUpperInvariant();
                }

                // If it's a snippet, expand it fully
                if (item.IconType == UI.Completion.CompletionIconType.Snippet && _snippetManager.TryGetSnippet(item.Text, out string fullSnippet))
                {
                    expansion = fullSnippet;
                }
                // ALTER context: item has full object definition in SnippetExpansion, replace from ALTER keyword start
                else if (!string.IsNullOrEmpty(item.SnippetExpansion) &&
                         (prevWord == "PROC" || prevWord == "PROCEDURE" || prevWord == "VIEW" || prevWord == "FUNCTION"))
                {
                    // Verify this is an ALTER statement by scanning backwards for ALTER keyword
                    string fullLineText = textBeforeCaret.TrimEnd();
                    int alterIdx = fullLineText.LastIndexOf("ALTER", StringComparison.OrdinalIgnoreCase);
                    if (alterIdx >= 0)
                    {
                        // Replace from ALTER start position to caret
                        wordStart = line.Start.Position + alterIdx;
                        currentWordLength = caretPosition.Position - wordStart;
                        expansion = item.SnippetExpansion;
                    }
                    else
                    {
                        expansion = item.SnippetExpansion;
                    }
                }
                // If it's a Database Object requested after an execution context, insert the full expanded parameter/column scaffolding
                else if ((item.IconType == UI.Completion.CompletionIconType.Table || 
                          item.IconType == UI.Completion.CompletionIconType.View || 
                          item.IconType == UI.Completion.CompletionIconType.Function ||
                          item.IconType == UI.Completion.CompletionIconType.StoredProcedure) && 
                          !string.IsNullOrEmpty(item.SnippetExpansion) &&
                          (prevWord == "EXEC" || prevWord == "EXECUTE" || prevWord == "INTO"))
                {
                    expansion = item.SnippetExpansion;
                }
                // If it's a normal Database Object, insert the full description (schema.name) which was stored there
                // EXCEPT if it's an alias ("Projected Alias", "Alias for "), in which case we just want the alias name (item.Text).
                else if ((item.IconType == UI.Completion.CompletionIconType.Table || 
                          item.IconType == UI.Completion.CompletionIconType.View || 
                          item.IconType == UI.Completion.CompletionIconType.Function ||
                          item.IconType == UI.Completion.CompletionIconType.StoredProcedure) && 
                          !string.IsNullOrEmpty(item.Description) && 
                          item.Description != "Built-in Function" && 
                          !item.Description.StartsWith("Alias for ") &&
                          !item.Description.StartsWith("Projected Alias"))
                {
                    expansion = item.Description;
                }
                // For Columns, the 'Text' is the raw column name which we want inserted directly.
                // Their 'Description' holds the Table name, which we do NOT want inserted on enter.
                // However, their SnippetExpansion might hold a mapped Alias.Column injection which we DO want to insert.
                else if (item.IconType == UI.Completion.CompletionIconType.Column)
                {
                    expansion = !string.IsNullOrEmpty(item.SnippetExpansion) ? item.SnippetExpansion : item.Text;
                }

                using (var edit = _textView.TextBuffer.CreateEdit())
                {
                    edit.Replace(wordStart, currentWordLength, expansion);
                    edit.Apply();
                }
                _completionWindow.Hide();
            }
            catch (Exception ex)
            {
                OutputWindowLogger.LogError("Failed to commit custom completion", ex);
            }
        }

        private void ResetAnalysisTimer()
        {
            if (SQLsensePackage.Settings?.EnableSqlGuardian == true)
            {
                _analysisTimer.Stop();
                _analysisTimer.Start();
            }
            else
            {
                // Ensure errors are cleared if feature is disabled
                _analysisTimer.Stop();
                Infrastructure.AnalysisErrorProvider.Instance.Clear();
            }
        }

        private void TriggerAnalysis()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    string sqlText = _textView.TextBuffer.CurrentSnapshot.GetText();
                    var results = _analyzer.Analyze(sqlText);

                    // Get file name for the error list (Requires UI Thread)
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    
                    _textView.TextBuffer.Properties.TryGetProperty(typeof(Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer), out Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer bufferAdapter);
                    string filePath = "SQL Query";
                    if (bufferAdapter is Microsoft.VisualStudio.Shell.Interop.IPersistFileFormat persistFile)
                    {
                        persistFile.GetCurFile(out filePath, out _);
                    }

                    AnalysisErrorProvider.Instance.UpdateErrors(results, filePath);
                }
                catch (Exception ex)
                {
                    OutputWindowLogger.LogError("Background analysis trigger failed", ex);
                }
            });
        }

        private bool TryExpandSnippet()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            try
            {
                var textBuffer = _textView.TextBuffer;
                var caretPosition = _textView.Caret.Position.BufferPosition;
                var line = caretPosition.GetContainingLine();
                
                int caretOffsetInLine = caretPosition.Position - line.Start.Position;
                string textBeforeCaret = line.GetText().Substring(0, caretOffsetInLine).TrimEnd();
                
                int lastSpaceIndex = textBeforeCaret.LastIndexOfAny(new char[] { ' ', '\t', '\n', '\r', '(', ')', ',', '=', '<', '>', '+' });
                string lastWord = lastSpaceIndex == -1 ? textBeforeCaret : textBeforeCaret.Substring(lastSpaceIndex + 1);

                if (!string.IsNullOrEmpty(lastWord) && _snippetManager.TryGetSnippet(lastWord, out string expansion))
                {
                    int wordStart = (lastSpaceIndex == -1 ? line.Start.Position : line.Start.Position + lastSpaceIndex + 1);
                    using (var edit = textBuffer.CreateEdit())
                    {
                        edit.Replace(wordStart, lastWord.Length, expansion);
                        edit.Apply();
                    }
                    OutputWindowLogger.Log($"Snippet expanded: {lastWord} -> {expansion.Trim()} (Trigger swallowed)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                OutputWindowLogger.LogError("Snippet expansion failed", ex);
            }
            
            return false;
        }

        private bool TryExpandWildcard()
        {
            try
            {
                var textBuffer = _textView.TextBuffer;
                var caretPosition = _textView.Caret.Position.BufferPosition;
                var line = caretPosition.GetContainingLine();
                string lineText = line.GetText();
                
                int caretOffsetInLine = caretPosition.Position - line.Start.Position;
                string textBeforeCaret = lineText.Substring(0, caretOffsetInLine).TrimEnd();

                // Quick exit if it doesn't end in an asterisk
                if (!textBeforeCaret.EndsWith("*")) return false;

                // Needs to be SELECT * FROM [TableName]
                // First, find the closest FROM before the asterisk (if user typed SELECT * FROM Table then went back to *)
                // Usually they type `SELECT * FROM Table`, then cursor is at end? 
                // Wait, if they are typing `SELECT * FROM Table`, the asterisk is no longer at the caret.
                // The Redgate SQL Prompt behavior is:
                // User types `SELECT * FROM Table`, then moves cursor to `*`, presses Tab OR
                // User presses Tab right after typing `*`? If they press Tab right after typing `*`, they haven't typed `FROM Table` yet.
                // So the asterisk expansion MUST scan the whole query forward to find the FROM clause.
                
                string fullText = textBuffer.CurrentSnapshot.GetText();
                int absoluteCaretPos = caretPosition.Position;
                
                // Extract the statement containing the caret
                // For simplicity without full AST parsing yet, let's grab the text from the last SELECT to the next semicolon or end of text
                int selectIdx = fullText.LastIndexOf("SELECT", absoluteCaretPos, StringComparison.OrdinalIgnoreCase);
                if (selectIdx == -1) return false;

                int asteriskPos = fullText.IndexOf("*", selectIdx);
                // The caret must be exactly right after the asterisk
                if (asteriskPos == -1 || absoluteCaretPos != asteriskPos + 1) return false;

                int fromIdx = fullText.IndexOf("FROM", absoluteCaretPos, StringComparison.OrdinalIgnoreCase);
                if (fromIdx == -1) return false;

                // Find the table name after FROM
                string afterFrom = fullText.Substring(fromIdx + 4).TrimStart();
                string[] wordsAfterFrom = afterFrom.Split(new[] { ' ', '\r', '\n', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                
                if (wordsAfterFrom.Length == 0) return false;

                string tableName = wordsAfterFrom[0].Replace("[", "").Replace("]", "");
                if (tableName.Contains(".")) tableName = tableName.Substring(tableName.LastIndexOf(".") + 1);

                // Fetch columns for this table
                var columns = SQLsense.Core.Completion.DatabaseSchemaProvider.GetCachedColumns()
                    .Where(c => !string.IsNullOrEmpty(c.Description) && 
                               (c.Description.EndsWith("." + tableName, StringComparison.OrdinalIgnoreCase) || 
                                c.Description.Equals(tableName, StringComparison.OrdinalIgnoreCase)))
                    .Select(c => c.Text)
                    .ToList();

                if (columns.Count > 0)
                {
                    string expansion = string.Join(", ", columns);
                    using (var edit = textBuffer.CreateEdit())
                    {
                        edit.Replace(asteriskPos, 1, expansion);
                        edit.Apply();
                    }
                    OutputWindowLogger.Log($"Wildcard expanded for table {tableName}: {columns.Count} columns.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                OutputWindowLogger.LogError("Wildcard expansion failed", ex);
            }
            return false;
        }

        private async System.Threading.Tasks.Task FormatKeywordsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Respect settings
                var casingSetting = SQLsensePackage.Settings?.KeywordCasing ?? UI.CasingStyle.Uppercase;
                if (casingSetting == UI.CasingStyle.None) return;

                var textBuffer = _textView.TextBuffer;
                var caretPosition = _textView.Caret.Position.BufferPosition;
                var line = caretPosition.GetContainingLine();
                string lineText = line.GetText();
                
                int caretOffsetInLine = caretPosition.Position - line.Start.Position;
                string textBeforeCaret = lineText.Substring(0, caretOffsetInLine).TrimEnd();
                
                int lastSpaceIndex = textBeforeCaret.LastIndexOfAny(new char[] { ' ', '\t', '\n', '\r', '(', ')', ',', '=', '<', '>', '+' });
                string lastWord = lastSpaceIndex == -1 ? textBeforeCaret : textBeforeCaret.Substring(lastSpaceIndex + 1);

                if (!string.IsNullOrEmpty(lastWord) && KeywordManager.IsKeyword(lastWord))
                {
                    // Apply casing based on setting
                    string casedWord = casingSetting == UI.CasingStyle.Lowercase 
                        ? lastWord.ToLowerInvariant() 
                        : lastWord.ToUpperInvariant();

                    if (lastWord != casedWord)
                    {
                        int wordStart = (lastSpaceIndex == -1 ? line.Start.Position : line.Start.Position + lastSpaceIndex + 1);
                        using (var edit = textBuffer.CreateEdit())
                        {
                            edit.Replace(wordStart, lastWord.Length, casedWord);
                            edit.Apply();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OutputWindowLogger.LogError("Keyword casing failed", ex);
            }
        }
    }
}
