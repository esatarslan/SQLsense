using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SQLsense.Core.Session;
using EnvDTE;
using EnvDTE80;
using Task = System.Threading.Tasks.Task;

namespace SQLsense
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(UI.GeneralOptionsPage), "SQLsense", "General", 0, 0, true)]
    [ProvideBindingPath]
    [Guid(PackageGuidString)]
    public sealed class SQLsensePackage : AsyncPackage
    {
        public const string PackageGuidString = "7A1D2C3B-4E5F-6A7B-8C9D-0E1F2A3B4C5D";

        public static UI.GeneralOptionsPage Settings { get; private set; }
        public bool IsShuttingDown { get; private set; }
        
        private SessionManager _sessionManager;
        private SessionTracker _sessionTracker;

        protected override int QueryClose(out bool canClose)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IsShuttingDown = true;
            if (_sessionTracker != null)
            {
                Infrastructure.OutputWindowLogger.Log("Package QueryClose triggered. Syncing sessions before shell tears down documents...");
                _sessionTracker.SyncAllDocuments();
            }
            return base.QueryClose(out canClose);
        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Settings = (UI.GeneralOptionsPage)GetDialogPage(typeof(UI.GeneralOptionsPage));
            
            Infrastructure.OutputWindowLogger.Initialize(this);
            Infrastructure.AnalysisErrorProvider.Initialize(this);
            Infrastructure.OutputWindowLogger.Log("SQLsense Package Initialized.");
            
            await FormatSqlCommand.InitializeAsync(this);
            await SettingsCommand.InitializeAsync(this);

            if (Settings?.EnableSessionRecovery == true)
            {
                _sessionManager = new SessionManager();
                _sessionTracker = new SessionTracker(this, _sessionManager);
                await _sessionTracker.InitializeAsync();
                
                // Run restoration with a slight delay to ensure SSMS is ready
                _ = System.Threading.Tasks.Task.Run(async () => {
                    await System.Threading.Tasks.Task.Delay(2000);
                    await JoinableTaskFactory.SwitchToMainThreadAsync();
                    await RestoreSessionsAsync();
                });
            }

            // Suppress native SSMS IntelliSense to prevent collision with our WPF box
            await SuppressNativeIntelliSenseAsync();
        }

        private async System.Threading.Tasks.Task SuppressNativeIntelliSenseAsync()
        {
            try
            {
                var dte = await GetServiceAsync(typeof(SDTE)) as DTE2;
                if (dte == null) return;

                string[] categories = { "TextEditor" };
                string[] subCategories = { "SQL", "Transact-SQL", "Transact-SQL-IntelliSense", "SQL Server Tools" };

                foreach (var cat in categories)
                {
                    foreach (var subCat in subCategories)
                    {
                        try
                        {
                            var props = dte.Properties[cat, subCat];
                            if (props != null)
                            {
                                foreach (Property prop in props)
                                {
                                    if (prop.Name.IndexOf("IntelliSense", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        prop.Name.IndexOf("AutoList", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        if (prop.Value is bool bVal && bVal == true)
                                        {
                                            prop.Value = false;
                                            Infrastructure.OutputWindowLogger.Log($"Disabled Native Setting: {cat}.{subCat}.{prop.Name}");
                                        }
                                    }
                                }
                            }
                        }
                        catch { /* Ignore if category doesn't exist */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Infrastructure.OutputWindowLogger.LogError("Failed to suppress Native IntelliSense via DTE", ex);
            }
        }

        private async System.Threading.Tasks.Task RestoreSessionsAsync()
        {
            // Switch to main thread for UI operations
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            
            try
            {
                var sessions = _sessionManager.GetAllSessions();
                Infrastructure.OutputWindowLogger.Log($"Found {sessions.Count} sessions to restore.");
                if (sessions.Count == 0) return;

                IVsUIShellOpenDocument openDoc = await GetServiceAsync(typeof(SVsUIShellOpenDocument)) as IVsUIShellOpenDocument;
                DTE2 dte = await GetServiceAsync(typeof(SDTE)) as DTE2;
                
                if (openDoc == null || dte == null)
                {
                    Infrastructure.OutputWindowLogger.Log("Could not get essential services (IVsUIShellOpenDocument or DTE).");
                    return;
                }

                foreach (var session in sessions)
                {
                    Infrastructure.OutputWindowLogger.Log($"Processing session: ID={session.Id}, Path='{session.FilePath}'");
                    
                    // Delete the old record. If restoration succeeds, the tracker will save it again with fresh session context.
                    _sessionManager.DeleteSession(session.Id);

                    if (!string.IsNullOrEmpty(session.FilePath) && File.Exists(session.FilePath))
                    {
                        Infrastructure.OutputWindowLogger.Log("Restoring disk file session.");
                        openDoc.OpenDocumentViaProject(session.FilePath, VSConstants.LOGVIEWID_Code, out _, out _, out _, out _);
                        Infrastructure.OutputWindowLogger.Log("Session restored successfully.");
                    }
                    else if (!string.IsNullOrEmpty(session.Content))
                    {
                        Infrastructure.OutputWindowLogger.Log("Unsaved session detected. Opening new query window...");
                        
                        try
                        {
                            dte.ExecuteCommand("File.NewQuery");
                            
                            // Wait for the new document to become active
                            EnvDTE.Document activeDoc = null;
                            for (int i = 0; i < 10; i++)
                            {
                                await System.Threading.Tasks.Task.Delay(200);
                                activeDoc = dte.ActiveDocument;
                                if (activeDoc != null) break;
                            }

                            if (activeDoc != null)
                            {
                                var textDoc = (EnvDTE.TextDocument)activeDoc.Object("TextDocument");
                                if (textDoc != null)
                                {
                                    var editPoint = textDoc.StartPoint.CreateEditPoint();
                                    editPoint.Delete(textDoc.EndPoint);
                                    editPoint.Insert(session.Content);
                                    Infrastructure.OutputWindowLogger.Log("Unsaved session content injected successfully.");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Infrastructure.OutputWindowLogger.Log($"Error restoring unsaved session: {ex.Message}");
                        }
                    }
                    else
                    {
                        Infrastructure.OutputWindowLogger.Log("Session has no path and no content. Skipping.");
                    }
                }
            }
            catch (Exception ex)
            {
                Infrastructure.OutputWindowLogger.Log($"RestoreSessions failure: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing)
            {
                _sessionTracker?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
