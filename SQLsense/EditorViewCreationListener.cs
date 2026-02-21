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
            // For debugging: this will show up if the listener actually loads
            // System.Windows.Forms.MessageBox.Show("SQLsense: Editor Listener Attached!");

            var textView = AdapterService.GetWpfTextView(textViewAdapter);
            if (textView == null) return;

            var filter = new EditorCommandFilter(textView);

            textViewAdapter.AddCommandFilter(filter, out IOleCommandTarget next);
            filter._nextCommandTarget = next;
        }
    }
}
