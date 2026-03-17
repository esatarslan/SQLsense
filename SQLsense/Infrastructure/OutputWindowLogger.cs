using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SQLsense.Infrastructure
{
    public static class OutputWindowLogger
    {
        private static IVsOutputWindowPane _pane;
        private static Guid _paneGuid = new Guid("11112222-3333-4444-5555-666677778888");

        public static void Initialize(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (serviceProvider.GetService(typeof(SVsOutputWindow)) is IVsOutputWindow outputWindow)
            {
                outputWindow.CreatePane(ref _paneGuid, "SQLsense", 1, 1);
                outputWindow.GetPane(ref _paneGuid, out _pane);
                _pane?.Activate(); // Make it visible
            }
        }

        public static void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[SQLsense Log] {message}");
        }

        public static void LogError(string message, Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SQLsense ERROR] {message}. Exception: {ex.Message}");
        }
    }
}
