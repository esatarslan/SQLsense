using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using SQLsense.Core;
using SQLsense.Infrastructure;

namespace SQLsense
{
    internal class EditorCommandFilter : IOleCommandTarget
    {
        private readonly IWpfTextView _textView;
        private readonly ISqlFormatter _formatter;
        internal IOleCommandTarget _nextCommandTarget;

        public EditorCommandFilter(IWpfTextView textView)
        {
            _textView = textView;
            _formatter = new SqlFormatter();
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _nextCommandTarget.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Execute the command first to let the character be typed
            int hresult = _nextCommandTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)nCmdID)
                {
                    case VSConstants.VSStd2KCmdID.TYPECHAR:
                        char typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
                        if (typedChar == ' ' || typedChar == '\t' || typedChar == '\r' || typedChar == '\n' || typedChar == ';')
                        {
                            _ = FormatCurrentContextAsync();
                        }
                        break;
                    case VSConstants.VSStd2KCmdID.RETURN:
                    case VSConstants.VSStd2KCmdID.TAB:
                        _ = FormatCurrentContextAsync();
                        break;
                }
            }

            return hresult;
        }

        private async System.Threading.Tasks.Task FormatCurrentContextAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var textBuffer = _textView.TextBuffer;
                var caretPosition = _textView.Caret.Position.BufferPosition;
                var line = caretPosition.GetContainingLine();
                string lineText = line.GetText();
                
                // Get the word just before the caret
                int caretOffsetInLine = caretPosition.Position - line.Start.Position;
                string textBeforeCaret = lineText.Substring(0, caretOffsetInLine).TrimEnd();
                
                int lastSpaceIndex = textBeforeCaret.LastIndexOfAny(new char[] { ' ', '\t', '\n', '\r', '(', ')', ',', '=', '<', '>', '+' });
                string lastWord = lastSpaceIndex == -1 ? textBeforeCaret : textBeforeCaret.Substring(lastSpaceIndex + 1);

                // 1. Immediate Keyword Casing (SQL Prompt Style)
                if (!string.IsNullOrEmpty(lastWord))
                {
                    if (KeywordManager.IsKeyword(lastWord))
                    {
                        string casedWord = KeywordManager.GetCasedKeyword(lastWord);
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

                // 2. Statement-level Formatting (Disabled for now as per user request)
                /*
                string currentFullText = textBuffer.CurrentSnapshot.GetText();
                if (string.IsNullOrWhiteSpace(currentFullText)) return;

                var formattedSql = _formatter.Format(currentFullText, out var errors);

                if (formattedSql != null && formattedSql != currentFullText)
                {
                    using (var edit = textBuffer.CreateEdit())
                    {
                        edit.Replace(0, textBuffer.CurrentSnapshot.Length, formattedSql);
                        edit.Apply();
                    }
                }
                */
            }
            catch (Exception ex)
            {
                OutputWindowLogger.LogError("Real-time casing failed", ex);
            }
        }
    }
}
