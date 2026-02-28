using System;

namespace SQLsense.UI.Completion
{
    public enum CompletionIconType
    {
        Keyword,
        Snippet,
        Table,
        View,
        StoredProcedure,
        Function,
        Column
    }

    public class CompletionItem
    {
        public string Text { get; set; }
        public string Description { get; set; }
        public string SnippetExpansion { get; set; }
        public CompletionIconType IconType { get; set; }

        public string IconColor
        {
            get
            {
                switch (IconType)
                {
                    case CompletionIconType.Snippet: return "#D08000"; // Orange
                    case CompletionIconType.Keyword: return "#00539C"; // Blue
                    case CompletionIconType.Table: return "#107C10"; // Green
                    case CompletionIconType.View: return "#038387"; // Teal
                    case CompletionIconType.StoredProcedure: return "#68217A"; // Purple
                    case CompletionIconType.Function: return "#B146C2"; // Magenta
                    case CompletionIconType.Column: return "#0078D7"; // Light Blue
                    default: return "#555555";
                }
            }
        }

        public string IconText
        {
            get
            {
                switch (IconType)
                {
                    case CompletionIconType.Snippet: return "</>";
                    case CompletionIconType.Keyword: return "Kw";
                    case CompletionIconType.Table: return "Tb";
                    case CompletionIconType.View: return "Vw";
                    case CompletionIconType.StoredProcedure: return "Sp";
                    case CompletionIconType.Function: return "Fn";
                    case CompletionIconType.Column: return "Col";
                    default: return "??";
                }
            }
        }

        public CompletionItem(string text, string description, CompletionIconType iconType)
        {
            Text = text;
            Description = description;
            IconType = iconType;
        }
    }
}
