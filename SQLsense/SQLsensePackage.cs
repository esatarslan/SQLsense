using System;
using System.IO;
using System.Linq;
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
        private EnvDTE.DTEEvents _dteEvents;

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

        private void DTEEvents_OnBeginShutdown()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IsShuttingDown = true;
            if (_sessionTracker != null)
            {
                Infrastructure.OutputWindowLogger.Log("DTE OnBeginShutdown triggered. Syncing sessions before IDE closes...");
                _sessionTracker.SyncAllDocuments();
            }
        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            try
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

                    var dte = await GetServiceAsync(typeof(SDTE)) as DTE2;
                    if (dte != null)
                    {
                        _dteEvents = dte.Events.DTEEvents;
                        _dteEvents.ModeChanged += (oldMode) => { /* keeping reference alive */ };
                        _dteEvents.OnBeginShutdown += DTEEvents_OnBeginShutdown;
                    }
                    
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
            catch (Exception ex)
            {
                string path = Path.Combine(Environment.GetEnvironmentVariable("TEMP"), "SQLsense_Init_Error.log");
                File.WriteAllText(path, ex.ToString());
                throw;
            }
        }

        private async System.Threading.Tasks.Task SuppressNativeIntelliSenseAsync()
        {
            try
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();

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
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            
            try
            {
                var sessions = _sessionManager.GetAllSessions();
                Infrastructure.OutputWindowLogger.Log($"Found {sessions.Count} sessions to restore.");
                if (sessions.Count == 0) return;

                DTE2 dte = await GetServiceAsync(typeof(SDTE)) as DTE2;
                IVsUIShellOpenDocument openDoc = await GetServiceAsync(typeof(SVsUIShellOpenDocument)) as IVsUIShellOpenDocument;

                if (dte == null)
                {
                    Infrastructure.OutputWindowLogger.Log("Could not get DTE service.");
                    return;
                }

                // Force-load SqlWorkbench.Interfaces from known SSMS path so reflection works
                System.Reflection.Assembly sqlWbAsm = EnsureSqlWorkbenchInterfacesLoaded();

                // Detect if SSMS has already restored documents natively (its own session recovery)
                int existingDocCount = 0;
                try { existingDocCount = dte.Documents.Count; } catch { }
                Infrastructure.OutputWindowLogger.Log($"SSMS already has {existingDocCount} open document(s) before our restore.");

                foreach (var session in sessions)
                {
                    Infrastructure.OutputWindowLogger.Log($"Processing: ID={session.Id}, Server='{session.ServerName}', Docs={existingDocCount}");
                    _sessionManager.DeleteSession(session.Id);

                    // ── Saved file ────────────────────────────────────────────
                    if (!string.IsNullOrEmpty(session.FilePath) && File.Exists(session.FilePath))
                    {
                        Infrastructure.OutputWindowLogger.Log("Restoring disk file session.");
                        openDoc?.OpenDocumentViaProject(session.FilePath, VSConstants.LOGVIEWID_Code, out _, out _, out _, out _);
                        continue;
                    }

                    // ── Unsaved content ───────────────────────────────────────
                    if (string.IsNullOrEmpty(session.Content)) continue;

                    if (existingDocCount > 0)
                    {
                        // SSMS already opened windows (either its own restore or user-opened tabs).
                        // Inject our content into the currently active document instead of opening new.
                        Infrastructure.OutputWindowLogger.Log("SSMS has open docs — injecting content into active doc.");
                        await InjectContentIntoActiveDocAsync(dte, session.Content);
                    }
                    else
                    {
                        // SSMS has no open windows — safe to open via IScriptFactory (no extra Connect dialog)
                        bool opened = TryOpenViaScriptFactory(sqlWbAsm, session);
                        if (!opened)
                        {
                            // Fallback only when SSMS has no windows — single Connect dialog is expected
                            Infrastructure.OutputWindowLogger.Log("IScriptFactory unavailable. Using File.NewQuery fallback.");
                            try { dte.ExecuteCommand("File.NewQuery"); } catch { }
                        }

                        await InjectContentIntoActiveDocAsync(dte, session.Content);
                    }

                    await System.Threading.Tasks.Task.Delay(300);
                }
            }
            catch (Exception ex)
            {
                Infrastructure.OutputWindowLogger.Log($"RestoreSessions failure: {ex.Message}");
            }
        }

        private System.Reflection.Assembly EnsureSqlWorkbenchInterfacesLoaded()
        {
            const string asmName = "SqlWorkbench.Interfaces";
            var existing = System.AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == asmName);
            if (existing != null) return existing;

            try
            {
                // Explicit path — SSMS always installs here
                string ssmsDir = Path.GetDirectoryName(typeof(SQLsensePackage).Assembly.Location);
                // Walk up to find SSMS IDE dir
                string idePath = @"C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE";
                string dllPath = Path.Combine(idePath, "SqlWorkbench.Interfaces.dll");
                if (File.Exists(dllPath))
                {
                    return System.Reflection.Assembly.LoadFrom(dllPath);
                }
            }
            catch (Exception ex)
            {
                Infrastructure.OutputWindowLogger.Log($"Could not load SqlWorkbench.Interfaces: {ex.Message}");
            }
            return null;
        }

        private bool TryOpenViaScriptFactory(System.Reflection.Assembly sqlWbAsm, SessionEntry session)
        {
            Infrastructure.OutputWindowLogger.Log($"[RestoreConn] Trying to open {session.ServerName} via IScriptFactory");
            if (sqlWbAsm == null || string.IsNullOrEmpty(session.ServerName)) 
            {
                Infrastructure.OutputWindowLogger.Log($"[RestoreConn] Failed: sqlWbAsm is null or ServerName is empty.");
                return false;
            }
            try
            {
                var factoryType = sqlWbAsm.GetTypes().FirstOrDefault(t => t.Name == "IScriptFactory");
                
                // UIConnectionInfo is in a different assembly! Search all loaded assemblies.
                var connInfoType = System.AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                    .FirstOrDefault(t => t.Name == "UIConnectionInfo");
                    
                var scriptTypeType = System.AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                    .FirstOrDefault(t => t.Name == "ScriptType");

                Infrastructure.OutputWindowLogger.Log($"[RestoreConn] factoryType: {factoryType?.Name}, connInfoType: {connInfoType?.Name}, scriptTypeType: {scriptTypeType?.Name}");

                if (factoryType == null || connInfoType == null || scriptTypeType == null) return false;

                var factory = Microsoft.VisualStudio.Shell.Package.GetGlobalService(factoryType);
                if (factory == null) 
                {
                    Infrastructure.OutputWindowLogger.Log($"[RestoreConn] Failed: IScriptFactory global service returned null.");
                    return false;
                }

                dynamic connInfo = Activator.CreateInstance(connInfoType);
                connInfo.ServerName = session.ServerName;
                connInfo.AuthenticationType = session.AuthType;
                if (session.AuthType != 0 && !string.IsNullOrEmpty(session.UserName))
                    connInfo.UserName = session.UserName;

                object scriptTypeSql = Enum.ToObject(scriptTypeType, 0); // 0 = Sql
                
                // Because CreateNewBlankScript is overloaded, we search by name and parameter count.
                var createMethod = factoryType.GetMethods().FirstOrDefault(m => 
                    m.Name == "CreateNewBlankScript" && 
                    m.GetParameters().Length == 3 && 
                    m.GetParameters()[1].ParameterType.Name == "UIConnectionInfo"
                );
                
                if (createMethod == null) 
                {
                    Infrastructure.OutputWindowLogger.Log($"[RestoreConn] Failed: createMethod is null (overload not found).");
                    return false;
                }

                createMethod.Invoke(factory, new object[] { scriptTypeSql, connInfo, null });
                Infrastructure.OutputWindowLogger.Log("[RestoreConn] SUCCESS: Opened via IScriptFactory with connection info.");
                return true;
            }
            catch (Exception ex)
            {
                Infrastructure.OutputWindowLogger.Log($"[RestoreConn] EXCEPTION: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private async System.Threading.Tasks.Task InjectContentIntoActiveDocAsync(DTE2 dte, string content)
        {
            EnvDTE.Document activeDoc = null;
            for (int i = 0; i < 10; i++)
            {
                await System.Threading.Tasks.Task.Delay(200);
                try { activeDoc = dte.ActiveDocument; } catch { }
                if (activeDoc != null) break;
            }
            if (activeDoc == null) return;
            try
            {
                var textDoc = activeDoc.Object("TextDocument") as EnvDTE.TextDocument;
                if (textDoc != null)
                {
                    var ep = textDoc.StartPoint.CreateEditPoint();
                    ep.Delete(textDoc.EndPoint);
                    ep.Insert(content);
                    Infrastructure.OutputWindowLogger.Log("Content injected successfully.");
                }
            }
            catch (Exception ex)
            {
                Infrastructure.OutputWindowLogger.Log($"Content injection failed: {ex.Message}");
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
