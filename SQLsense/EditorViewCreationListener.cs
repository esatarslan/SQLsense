using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace SQLsense
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("code")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class EditorViewCreationListener : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService AdapterService = null;

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            var textView = AdapterService.GetWpfTextView(textViewAdapter);
            if (textView == null) return;

            // Prevent double-attachment if MEF or SSMS fires this multiple times for the same view
            if (textView.Properties.ContainsProperty(typeof(EditorCommandFilter)))
                return;

            var filter = new EditorCommandFilter(textView);
            textView.Properties.AddProperty(typeof(EditorCommandFilter), filter);

            textViewAdapter.AddCommandFilter(filter, out IOleCommandTarget next);
            filter._nextCommandTarget = next;

            // Start preloading the database schema in the background so it's ready for auto-complete
            _ = System.Threading.Tasks.Task.Run(() => {
                try {
                    SQLsense.Core.Completion.DatabaseSchemaProvider.TriggerRefreshInBackground();
                } catch { }
            });
        }
    }
}
