using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;

namespace SQLsense.UI.Completion
{
    public partial class CompletionWindow : Window
    {
        public event EventHandler<CompletionItem> ItemSelected;
        public event EventHandler ClosedByUser;

        public CompletionWindow()
        {
            InitializeComponent();
            CompletionList.MouseDoubleClick += (s, e) => CommitSelection();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_NOACTIVATE = 3;

            if (msg == WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(MA_NOACTIVATE);
            }

            return IntPtr.Zero;
        }

        public void SetItems(List<CompletionItem> items, bool selectFirst = false)
        {
            if (items == null || items.Count == 0)
            {
                CompletionList.ItemsSource = null;
                return;
            }

            CompletionList.ItemsSource = items;
            CompletionList.SelectedIndex = selectFirst ? 0 : -1;
            if (selectFirst)
            {
                CompletionList.ScrollIntoView(CompletionList.SelectedItem);
            }
        }

        public bool HasSelection => CompletionList.SelectedIndex != -1;

        public void MoveUp()
        {
            if (CompletionList.Items.Count == 0) return;
            if (CompletionList.SelectedIndex > 0)
            {
                CompletionList.SelectedIndex--;
                CompletionList.ScrollIntoView(CompletionList.SelectedItem);
            }
        }

        public void MoveDown()
        {
            if (CompletionList.Items.Count == 0) return;
            if (CompletionList.SelectedIndex < CompletionList.Items.Count - 1)
            {
                CompletionList.SelectedIndex++;
                CompletionList.ScrollIntoView(CompletionList.SelectedItem);
            }
        }

        public void MovePageUp()
        {
            if (CompletionList.Items.Count == 0) return;
            int newIndex = Math.Max(0, CompletionList.SelectedIndex - 10);
            CompletionList.SelectedIndex = newIndex;
            CompletionList.ScrollIntoView(CompletionList.SelectedItem);
        }

        public void MovePageDown()
        {
            if (CompletionList.Items.Count == 0) return;
            int newIndex = Math.Min(CompletionList.Items.Count - 1, CompletionList.SelectedIndex + 10);
            CompletionList.SelectedIndex = newIndex;
            CompletionList.ScrollIntoView(CompletionList.SelectedItem);
        }

        public void CommitSelection()
        {
            if (CompletionList.SelectedItem is CompletionItem item)
            {
                ItemSelected?.Invoke(this, item);
            }
        }

        public void Cancel()
        {
            ClosedByUser?.Invoke(this, EventArgs.Empty);
            this.Hide();
        }
    }
}
