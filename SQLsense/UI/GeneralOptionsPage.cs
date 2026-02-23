using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SQLsense.UI
{
    public enum CasingStyle
    {
        Uppercase,
        Lowercase,
        None
    }

    [Guid("D1E2C3B4-5A6B-7C8D-9E0F-1A2B3C4D5E6F")]
    public class GeneralOptionsPage : DialogPage
    {
        private CasingStyle _keywordCasing = CasingStyle.Uppercase;
        private int _indentationSize = 4;
        private bool _enableSqlGuardian = false;
        private bool _enableSessionRecovery = false;
        private bool _includeSemicolons = true;
        private bool _newLineBeforeFromClause = true;
        private bool _newLineBeforeWhereClause = true;
        private bool _newLineBeforeJoinClause = true;

        [Category("Session Recovery")]
        [DisplayName("Enable Session Recovery")]
        [Description("Automatically restores open SQL sekmeler when SSMS restarts.")]
        [DefaultValue(false)]
        public bool EnableSessionRecovery
        {
            get { return _enableSessionRecovery; }
            set { _enableSessionRecovery = value; }
        }

        [Category("SQL Guardian (Analysis)")]
        [DisplayName("Enable SQL Guardian")]
        [Description("Enables real-time SQL analysis to detect performance and safety issues.")]
        [DefaultValue(false)]
        public bool EnableSqlGuardian
        {
            get { return _enableSqlGuardian; }
            set { _enableSqlGuardian = value; }
        }

        [Category("SQL Formatting")]
        [DisplayName("Keyword Casing")]
        [Description("Defines how SQL keywords should be cased. 'None' disables real-time casing.")]
        [DefaultValue(CasingStyle.Uppercase)]
        public CasingStyle KeywordCasing
        {
            get { return _keywordCasing; }
            set { _keywordCasing = value; }
        }

        [Category("SQL Formatting")]
        [DisplayName("Indentation Size")]
        [Description("Number of spaces used for indentation.")]
        [DefaultValue(4)]
        public int IndentationSize
        {
            get { return _indentationSize; }
            set { _indentationSize = value; }
        }

        [Category("SQL Formatting")]
        [DisplayName("Include Semicolons")]
        [Description("Adds missing semicolons to statements.")]
        [DefaultValue(true)]
        public bool IncludeSemicolons
        {
            get { return _includeSemicolons; }
            set { _includeSemicolons = value; }
        }

        [Category("Query Structure")]
        [DisplayName("New Line Before FROM")]
        [Description("Places the FROM clause on a new line.")]
        [DefaultValue(true)]
        public bool NewLineBeforeFromClause
        {
            get { return _newLineBeforeFromClause; }
            set { _newLineBeforeFromClause = value; }
        }

        [Category("Query Structure")]
        [DisplayName("New Line Before WHERE")]
        [Description("Places the WHERE clause on a new line.")]
        [DefaultValue(true)]
        public bool NewLineBeforeWhereClause
        {
            get { return _newLineBeforeWhereClause; }
            set { _newLineBeforeWhereClause = value; }
        }

        [Category("Query Structure")]
        [DisplayName("New Line Before JOIN")]
        [Description("Places JOIN clauses on a new line.")]
        [DefaultValue(true)]
        public bool NewLineBeforeJoinClause
        {
            get { return _newLineBeforeJoinClause; }
            set { _newLineBeforeJoinClause = value; }
        }
    }
}
