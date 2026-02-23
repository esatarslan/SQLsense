using System;
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
        internal IOleCommandTarget _nextCommandTarget;

        public EditorCommandFilter(IWpfTextView textView)
        {
            _textView = textView;
            _formatter = new SqlFormatter();
            _snippetManager = new SnippetManager();
            _analyzer = new SqlAnalyzer();

            _analysisTimer = new System.Timers.Timer(1000); // Analyze after 1 second of inactivity
            _analysisTimer.AutoReset = false;
            _analysisTimer.Elapsed += (s, e) => TriggerAnalysis();
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _nextCommandTarget.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)nCmdID)
                {
                    case VSConstants.VSStd2KCmdID.TYPECHAR:
                        char typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
                        if (typedChar == ' ' || typedChar == '\t')
                        {
                            if (TryExpandSnippet()) return VSConstants.S_OK; // Swallow space/tab if expanded
                        }
                        break;
                    case VSConstants.VSStd2KCmdID.RETURN:
                    case VSConstants.VSStd2KCmdID.TAB:
                    case VSConstants.VSStd2KCmdID.BACKTAB:
                        if (TryExpandSnippet()) return VSConstants.S_OK; // Swallow if expanded
                        break;
                }
            }

            // Let the character be typed
            int hresult = _nextCommandTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

            // Trigger async keyword casing and research analysis timer
            if (hresult == VSConstants.S_OK && pguidCmdGroup == VSConstants.VSStd2K)
            {
                var cmdId = (VSConstants.VSStd2KCmdID)nCmdID;
                if (cmdId == VSConstants.VSStd2KCmdID.TYPECHAR || cmdId == VSConstants.VSStd2KCmdID.RETURN || cmdId == VSConstants.VSStd2KCmdID.TAB)
                {
                    _ = FormatKeywordsAsync();
                    ResetAnalysisTimer();
                }
            }

            return hresult;
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
