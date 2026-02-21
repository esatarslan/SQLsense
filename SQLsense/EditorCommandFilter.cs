using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace SQLsense
{
    internal class EditorCommandFilter : IOleCommandTarget
    {
        private readonly IWpfTextView _textView;
        private readonly SqlFormatter _formatter;
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

        private static readonly string[] SqlKeywords = { 
            "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUTHORIZATION", "BACKUP", "BEGIN", "BETWEEN", "BREAK", "BROWSE", "BULK", "BY", 
            "CASCADE", "CASE", "CHECK", "CHECKPOINT", "CLOSE", "CLUSTERED", "COALESCE", "COLLATE", "COLUMN", "COMMIT", "COMPUTE", "CONSTRAINT", 
            "CONTAINS", "CONTAINSTABLE", "CONTINUE", "CONVERT", "CREATE", "CROSS", "CURRENT", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", 
            "CURRENT_USER", "CURSOR", "DATABASE", "DBCC", "DEALLOCATE", "DECLARE", "DEFAULT", "DELETE", "DENY", "DESC", "DISK", "DISTINCT", 
            "DISTRIBUTED", "DOUBLE", "DROP", "DUMMY", "DUMP", "ELSE", "END", "ERRLVL", "EXCEPT", "EXEC", "EXECUTE", "EXISTS", "EXIT", "FETCH", 
            "FILE", "FILLFACTOR", "FOR", "FOREIGN", "FREETEXT", "FREETEXTTABLE", "FROM", "FULL", "FUNCTION", "GOTO", "GRANT", "GROUP", "HAVING", 
            "HOLDLOCK", "IDENTITY", "IDENTITY_INSERT", "IDENTITYCOL", "IF", "IN", "INDEX", "INNER", "INSERT", "INTERSECT", "INTO", "IS", "JOIN", 
            "KEY", "KILL", "LEFT", "LIKE", "LINENO", "LOAD", "NATIONAL", "NOCHECK", "NONCLUSTERED", "NOT", "NULL", "NULLIF", "OF", "OFF", 
            "OFFSETS", "ON", "OPEN", "OPENDATASOURCE", "OPENQUERY", "OPENROWSET", "OPENXML", "OPTION", "OR", "ORDER", "OUTER", "OVER", 
            "PERCENT", "PLAN", "PRECISION", "PRIMARY", "PRINT", "PROC", "PROCEDURE", "PUBLIC", "RAISERROR", "READ", "READTEXT", "RECONFIGURE", 
            "REFERENCES", "REPLICATION", "RESTORE", "RESTRICT", "RETURN", "REVOKE", "RIGHT", "ROLLBACK", "ROWCOUNT", "ROWGUIDCOL", "RULE", 
            "SAVE", "SCHEMA", "SELECT", "SESSION_USER", "SET", "SETUSER", "SHUTDOWN", "SOME", "STATISTICS", "SYSTEM_USER", "TABLE", "TEXTSIZE", 
            "THEN", "TO", "TOP", "TRAN", "TRANSACTION", "TRIGGER", "TRUNCATE", "TSEQUAL", "UNION", "UNIQUE", "UPDATE", "UPDATETEXT", "USE", 
            "USER", "VALUES", "VARYING", "VIEW", "WAITFOR", "WHEN", "WHERE", "WHILE", "WITH",
            "VARCHAR", "NVARCHAR", "INT", "BIGINT", "SMALLINT", "TINYINT", "BIT", "DECIMAL", "NUMERIC", "MONEY", "SMALLMONEY", "FLOAT", 
            "REAL", "DATETIME", "DATETIME2", "DATETIMEOFFSET", "DATE", "TIME", "CHAR", "NCHAR", "BINARY", "VARBINARY", "IMAGE", "TEXT", 
            "NTEXT", "XML", "MAX", "GO", "PIVOT", "UNPIVOT", "MERGE", "OUTPUT", "USE"
        };

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
                    foreach (var keyword in SqlKeywords)
                    {
                        if (string.Equals(lastWord, keyword, StringComparison.OrdinalIgnoreCase) && lastWord != keyword.ToUpperInvariant())
                        {
                            int wordStart = (lastSpaceIndex == -1 ? line.Start.Position : line.Start.Position + lastSpaceIndex + 1);
                            using (var edit = textBuffer.CreateEdit())
                            {
                                edit.Replace(wordStart, lastWord.Length, keyword.ToUpperInvariant());
                                edit.Apply();
                            }
                            return; // Wait for next trigger for full formatting
                        }
                    }
                }

                // 2. Statement-level Formatting (Disabled for now as per user request)
                /*
                string currentFullText = textBuffer.CurrentSnapshot.GetText();
                if (string.IsNullOrWhiteSpace(currentFullText)) return;

                var formattedSql = _formatter.Format(currentFullText, out var errors);

                // Only apply if there are no errors (we don't want to format broken syntax in real-time)
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
            catch (Exception)
            {
                // Fail silently
            }
        }
    }
}
