using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace SQLsense
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideBindingPath]
    [Guid(PackageGuidString)]
    public sealed class SQLsensePackage : AsyncPackage
    {
        public const string PackageGuidString = "7A1D2C3B-4E5F-6A7B-8C9D-0E1F2A3B4C5D";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            
            Infrastructure.OutputWindowLogger.Initialize(this);
            Infrastructure.OutputWindowLogger.Log("SQLsense Package Initialized.");
            
            await FormatSqlCommand.InitializeAsync(this);
        }
    }
}
