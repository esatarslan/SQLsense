using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using System.Runtime.InteropServices;
using Task = System.Threading.Tasks.Task;

namespace SQLsense
{
    internal sealed class FormatSqlCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e");

        private readonly AsyncPackage package;

        private FormatSqlCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static FormatSqlCommand Instance { get; private set; }

        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider => this.package;

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new FormatSqlCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            // Get the text manager service
            if (!(((System.IServiceProvider)ServiceProvider).GetService(typeof(SVsTextManager)) is IVsTextManager textManager))
            {
                ShowMessage("Could not retrieve text manager.");
                return;
            }

            // Get the active text view
            textManager.GetActiveView(1, null, out IVsTextView activeView);
            if (activeView == null)
            {
                ShowMessage("No active query window found.");
                return;
            }

            // Get the text lines buffer
            activeView.GetBuffer(out IVsTextLines textLines);
            if (textLines == null) return;

            // Read the full text
            textLines.GetLastLineIndex(out int lastLine, out int lastIndex);
            textLines.GetLineText(0, 0, lastLine, lastIndex, out string sqlText);

            if (string.IsNullOrWhiteSpace(sqlText))
            {
                ShowMessage("The query window is empty.");
                return;
            }

            // Format the SQL Text
            var formatter = new SqlFormatter();
            var formattedSql = formatter.Format(sqlText, out var errors);

            if (errors != null && errors.Count > 0)
            {
                string errorMsg = $"Found {errors.Count} syntax errors!\n\nFirst error: {errors[0].Message} at Line {errors[0].Line}, Column {errors[0].Column}";
                ShowMessage(errorMsg, OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            if (formattedSql != null)
            {
                // Replace the text in the buffer
                IntPtr pText = Marshal.StringToHGlobalUni(formattedSql);
                try
                {
                    // Replace the entire buffer from start to end
                    textLines.ReplaceLines(0, 0, lastLine, lastIndex, pText, formattedSql.Length, null);
                }
                finally
                {
                    Marshal.FreeHGlobal(pText);
                }
            }
            else
            {
                ShowMessage("Formatting failed for an unknown reason.", OLEMSGICON.OLEMSGICON_WARNING);
            }
        }

        private void ShowMessage(string message, OLEMSGICON icon = OLEMSGICON.OLEMSGICON_INFO)
        {
            VsShellUtilities.ShowMessageBox(
                this.package,
                message,
                "SQLsense Parser",
                icon,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
