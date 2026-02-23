using System;
using System.IO;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace SQLsense.Core.Session
{
    public class SessionTracker : IVsRunningDocTableEvents, IDisposable
    {
        private readonly SQLsensePackage _package;
        private readonly SessionManager _sessionManager;
        private IVsRunningDocumentTable _rdt;
        private uint _rdtCookie;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, string> _cookieToPathCache = new System.Collections.Concurrent.ConcurrentDictionary<uint, string>();

        public SessionTracker(SQLsensePackage package, SessionManager sessionManager)
        {
            _package = package;
            _sessionManager = sessionManager;
        }

        public async System.Threading.Tasks.Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _rdt = await _package.GetServiceAsync(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            
            if (_rdt != null)
            {
                _rdt.AdviseRunningDocTableEvents(this, out _rdtCookie);
            }
        }

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            TrackDocument(docCookie);
            return VSConstants.S_OK;
        }

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            // In VS/SSMS, the last unlock that closes the document usually happens when dwEditLocksRemaining is 1 
            // and we are requested to unlock an edit lock.
            bool isLastEditUnlock = (dwEditLocksRemaining == 1) && ((dwRDTLockType & (uint)_VSRDTFLAGS.RDT_EditLock) != 0);
            
            Infrastructure.OutputWindowLogger.Log($"Unlock: Cookie={docCookie}, Locks={dwEditLocksRemaining}, Type={dwRDTLockType}, IsLast={isLastEditUnlock}");

            if (isLastEditUnlock)
            {
                RemoveDocument(docCookie);
            }
            return VSConstants.S_OK;
        }

        public int OnAfterSave(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            TrackDocument(docCookie);
            return VSConstants.S_OK;
        }

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            TrackDocument(docCookie);
            return VSConstants.S_OK;
        }

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
        {
            return VSConstants.S_OK;
        }

        private void TrackDocument(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            if (SQLsensePackage.Settings?.EnableSessionRecovery != true) return;

            _rdt.GetDocumentInfo(docCookie, out _, out _, out _, out string pbstrMkDocument, out _, out _, out _);
            
            if (string.IsNullOrEmpty(pbstrMkDocument)) return;

            // In SSMS, unsaved queries might not have .sql extension in their mark
            // but we want to capture them if they are SQL editors.
            if (!pbstrMkDocument.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && 
                !pbstrMkDocument.StartsWith("query:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Infrastructure.OutputWindowLogger.Log($"Tracking SQL document: {pbstrMkDocument}");
            
            // Cache the path for reliable deletion later
            _cookieToPathCache[docCookie] = pbstrMkDocument;

            // Get text content
            var text = GetTextFromDocCookie(docCookie);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _sessionManager.SaveSession(new SessionEntry
            {
                Id = pbstrMkDocument,
                FilePath = pbstrMkDocument,
                Content = text,
                LastUpdated = DateTime.Now
            });
        }

        public void SyncAllDocuments()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Infrastructure.OutputWindowLogger.Log("Syncing all open documents using RDT iteration...");
            
            if (SQLsensePackage.Settings?.EnableSessionRecovery != true) return;

            // Clear the DB entirely so we only save what is TRULY open at this exact moment.
            _sessionManager.ClearAll();

            if (_rdt != null)
            {
                if (_rdt.GetRunningDocumentsEnum(out IEnumRunningDocuments enumRdt) == VSConstants.S_OK && enumRdt != null)
                {
                    uint[] cookie = new uint[1];
                    uint fetched = 0;

                    while (enumRdt.Next(1, cookie, out fetched) == VSConstants.S_OK && fetched == 1)
                    {
                        TrackDocument(cookie[0]);
                    }
                }
            }
            Infrastructure.OutputWindowLogger.Log("Shutdown sync complete. Database perfectly matches active tabs.");
        }

        private void RemoveDocument(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            if (IsShuttingDown())
            {
                Infrastructure.OutputWindowLogger.Log("Shutdown detected. Skipping document removal.");
                return;
            }

            if (_cookieToPathCache.TryRemove(docCookie, out string path))
            {
                Infrastructure.OutputWindowLogger.Log($"Removing document from session: {path}");
                _sessionManager.DeleteSession(path);
            }
            else
            {
                // Fallback attempt
                _rdt.GetDocumentInfo(docCookie, out _, out _, out _, out string pbstrMkDocument, out _, out _, out _);
                if (!string.IsNullOrEmpty(pbstrMkDocument))
                {
                    Infrastructure.OutputWindowLogger.Log($"Removing document from session (fallback): {pbstrMkDocument}");
                    _sessionManager.DeleteSession(pbstrMkDocument);
                }
            }
        }

        private bool IsShuttingDown()
        {
            return _package.IsShuttingDown;
        }

        private string GetTextFromDocCookie(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _rdt.GetDocumentInfo(docCookie, out _, out _, out _, out _, out IVsHierarchy hierarchy, out _, out IntPtr docData);

            if (docData != IntPtr.Zero)
            {
                object docDataObject = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(docData);
                if (docDataObject is IVsTextLines buffer)
                {
                    if (buffer.GetLineCount(out int lineCount) == VSConstants.S_OK)
                    {
                        if (buffer.GetLengthOfLine(lineCount - 1, out int lastLineLength) == VSConstants.S_OK)
                        {
                            if (buffer.GetLineText(0, 0, lineCount - 1, lastLineLength, out string text) == VSConstants.S_OK)
                            {
                                return text;
                            }
                        }
                    }
                }
            }
            return null;
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_rdt != null && _rdtCookie != 0)
            {
                _rdt.UnadviseRunningDocTableEvents(_rdtCookie);
                _rdtCookie = 0;
            }
        }
    }
}
