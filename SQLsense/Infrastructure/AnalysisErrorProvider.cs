using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shell;
using SQLsense.Core.Analysis;

namespace SQLsense.Infrastructure
{
    public class AnalysisErrorProvider : IDisposable
    {
        private readonly ErrorListProvider _errorListProvider;
        private static AnalysisErrorProvider _instance;

        private AnalysisErrorProvider(IServiceProvider serviceProvider)
        {
            _errorListProvider = new ErrorListProvider(serviceProvider)
            {
                ProviderName = "SQLsense Analyzer",
                ProviderGuid = new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D")
            };
        }

        public static void Initialize(IServiceProvider serviceProvider)
        {
            if (_instance == null)
            {
                _instance = new AnalysisErrorProvider(serviceProvider);
            }
        }

        public static AnalysisErrorProvider Instance => _instance;

        public void Clear()
        {
            _errorListProvider.Tasks.Clear();
        }

        public void UpdateErrors(IEnumerable<SqlAnalysisResult> results, string fileName)
        {
            ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                _errorListProvider.SuspendRefresh();
                try
                {
                    _errorListProvider.Tasks.Clear();

                    foreach (var result in results)
                    {
                        var task = new ErrorTask
                        {
                            Category = TaskCategory.CodeSense,
                            ErrorCategory = MapSeverity(result.Severity),
                            Text = $"[SQLsense] {result.Message} ({result.RuleId})",
                            Line = result.Line - 1,   // 0-indexed
                            Column = result.Column - 1, // 0-indexed
                            Document = fileName,
                            Priority = TaskPriority.Normal
                        };

                        task.Navigate += (sender, e) =>
                        {
                            _errorListProvider.Navigate(task, new Guid(EnvDTE.Constants.vsViewKindCode));
                        };

                        _errorListProvider.Tasks.Add(task);
                    }
                }
                finally
                {
                    _errorListProvider.ResumeRefresh();
                    if (_errorListProvider.Tasks.Count > 0)
                    {
                        _errorListProvider.Show();
                    }
                }
            });
        }

        private TaskErrorCategory MapSeverity(AnalysisSeverity severity)
        {
            switch (severity)
            {
                case AnalysisSeverity.Error: return TaskErrorCategory.Error;
                case AnalysisSeverity.Warning: return TaskErrorCategory.Warning;
                default: return TaskErrorCategory.Message;
            }
        }

        public void Dispose()
        {
            _errorListProvider?.Dispose();
        }
    }
}
