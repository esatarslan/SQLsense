using System.Collections.Generic;

namespace SQLsense.Core.Analysis
{
    public enum AnalysisSeverity
    {
        Info,
        Warning,
        Error
    }

    public class SqlAnalysisResult
    {
        public string Message { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public AnalysisSeverity Severity { get; set; }
        public string RuleId { get; set; }
    }

    public interface ISqlAnalyzer
    {
        IEnumerable<SqlAnalysisResult> Analyze(string sql);
    }
}
