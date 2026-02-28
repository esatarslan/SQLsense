using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SQLsense.UI.Completion;
using SQLsense.Infrastructure;

namespace SQLsense.Core.Completion
{
    public static class DatabaseSchemaProvider
    {
        private static string _lastConnStr;
        private static List<CompletionItem> _cachedObjects = new List<CompletionItem>();
        private static DateTime _lastRefresh = DateTime.MinValue;
        private static bool _isFetching = false;

        public static string GetActiveConnectionString()
        {
            try
            {
                var dte = Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
                if (dte?.ActiveDocument?.ActiveWindow?.Object == null) return null;

                var obj = dte.ActiveDocument.ActiveWindow.Object;
                var connProp = obj.GetType().GetProperty("Connection", BindingFlags.Public | BindingFlags.Instance);
                var uici = connProp?.GetValue(obj);
                if (uici == null) return null;

                var type = uici.GetType();
                string serverName = type.GetProperty("ServerName")?.GetValue(uici) as string;
                int authType = (int)(type.GetProperty("AuthenticationType")?.GetValue(uici) ?? 0);
                string userName = type.GetProperty("UserName")?.GetValue(uici) as string;
                string password = type.GetProperty("Password")?.GetValue(uici) as string;

                var advOpts = type.GetProperty("AdvancedOptions")?.GetValue(uici) as System.Collections.Specialized.NameValueCollection;
                string database = null;

                if (advOpts != null && advOpts["DATABASE"] != null)
                {
                    database = advOpts["DATABASE"];
                }
                
                if (string.IsNullOrEmpty(database))
                {
                    database = type.GetProperty("DatabaseName")?.GetValue(uici) as string;
                }

                 if (string.IsNullOrEmpty(database) || database.Equals("master", StringComparison.OrdinalIgnoreCase))
                {
                   // Another fallback strategy for SSMS
                   var dteDb = dte.ActiveDocument?.ProjectItem?.Properties?.Item("Database")?.Value as string;
                   if (!string.IsNullOrEmpty(dteDb)) database = dteDb;
                }

                if (string.IsNullOrEmpty(database)) database = "master";

                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = serverName,
                    InitialCatalog = database,
                    Encrypt = false,
                    ConnectTimeout = 3
                };

                if (authType == 0 || authType == 3) // Windows Auth
                {
                    builder.IntegratedSecurity = true;
                }
                else
                {
                    builder.UserID = userName;
                    builder.Password = password;
                }

                string cs = builder.ConnectionString;
                return cs;
            }
            catch (Exception ex)
            {
                OutputWindowLogger.LogError("Error extracting connection string", ex);
                return null;
            }
        }

        private static List<CompletionItem> _cachedColumns = new List<CompletionItem>();

        public static List<CompletionItem> GetCachedObjects()
        {
            TriggerRefreshInBackground();
            return _cachedObjects;
        }

        public static List<CompletionItem> GetCachedColumns()
        {
            TriggerRefreshInBackground();
            return _cachedColumns;
        }

        public static void TriggerRefreshInBackground()
        {
            if (_isFetching) return;

            string connStr = GetActiveConnectionString();
            if (string.IsNullOrEmpty(connStr)) return;

            if (_lastConnStr == connStr && (DateTime.Now - _lastRefresh).TotalSeconds < 60)
            {
                return;
            }

            _isFetching = true;
            Task.Run(() => 
            {
                try
                {
                    var newObjects = new List<CompletionItem>();
                    var newColumns = new List<CompletionItem>();

                    using (var conn = new SqlConnection(connStr))
                    {
                        conn.Open();

                        // 1. Fetch Objects (Tables, Views, SPs, Functs) with their parameters/columns pre-aggregated
                        using (var cmd = new SqlCommand(@"
                            SELECT 
                                s.name AS SchemaName, 
                                o.name AS ObjectName, 
                                o.type_desc,
                                (
                                    SELECT STRING_AGG(p.name, ', ')
                                    FROM sys.parameters p
                                    WHERE p.object_id = o.object_id
                                ) AS Parameters,
                                (
                                    SELECT STRING_AGG(c.name, ', ')
                                    FROM sys.columns c
                                    WHERE c.object_id = o.object_id AND c.is_identity = 0 AND c.is_computed = 0
                                ) AS Columns
                            FROM sys.objects o
                            INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                            WHERE o.type IN ('U', 'V', 'P', 'FN', 'TF', 'IF')
                            ORDER BY o.name
                        ", conn))
                        {
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string schema = reader["SchemaName"].ToString();
                                    string name = reader["ObjectName"].ToString();
                                    string typeDesc = reader["type_desc"].ToString();
                                    string parameters = reader["Parameters"]?.ToString() ?? "";
                                    string tblColumns = reader["Columns"]?.ToString() ?? "";
                                    
                                    CompletionIconType icon = CompletionIconType.Keyword;
                                    string snippetExpansion = null;

                                    if (typeDesc.Contains("TABLE")) 
                                    {
                                        icon = CompletionIconType.Table;
                                        if (!string.IsNullOrEmpty(tblColumns))
                                        {
                                            var cols = tblColumns.Split(',');
                                            var valuesText = string.Join(", ", cols.Select(c => " "));
                                            snippetExpansion = $"{schema}.[{name}] ({tblColumns}) VALUES ({valuesText})";
                                        }
                                    }
                                    else if (typeDesc.Contains("VIEW")) 
                                    {
                                        icon = CompletionIconType.View;
                                    }
                                    else if (typeDesc.Contains("PROCEDURE")) 
                                    {
                                        icon = CompletionIconType.StoredProcedure;
                                        if (!string.IsNullOrEmpty(parameters))
                                        {
                                            // Split parameters and format: @Param1 = , @Param2 = 
                                            var prs = string.Join(", ", parameters.Split(',').Select(p => p.Trim() + " = "));
                                            snippetExpansion = $"{schema}.{name} {prs}";
                                        }
                                        else
                                        {
                                            snippetExpansion = $"{schema}.{name}";
                                        }
                                    }
                                    else if (typeDesc.Contains("FUNCTION")) 
                                    {
                                        icon = CompletionIconType.Function;
                                        if (!string.IsNullOrEmpty(parameters))
                                        {
                                            snippetExpansion = $"{schema}.{name}({parameters})";
                                        }
                                        else
                                        {
                                            snippetExpansion = $"{schema}.{name}()";
                                        }
                                    }

                                    var item = new CompletionItem(name, $"{schema}.{name}", icon);
                                    if (snippetExpansion != null) item.SnippetExpansion = snippetExpansion;
                                    
                                    newObjects.Add(item);
                                }
                            }
                        }

                        // 2. Fetch Columns for Tables and Views
                        using (var cmd = new SqlCommand(@"
                            SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName
                            FROM sys.columns c
                            INNER JOIN sys.objects t ON c.object_id = t.object_id
                            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                            WHERE t.type IN ('U', 'V')
                        ", conn))
                        {
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string schema = reader["SchemaName"].ToString();
                                    string tableName = reader["TableName"].ToString();
                                    string colName = reader["ColumnName"].ToString();
                                    
                                    newColumns.Add(new CompletionItem(colName, $"{schema}.{tableName}", CompletionIconType.Column));
                                }
                            }
                        }
                    }

                    _cachedObjects = newObjects;
                    _cachedColumns = newColumns;
                    _lastConnStr = connStr;
                    _lastRefresh = DateTime.Now;
                }
                catch (Exception ex)
                {
                    OutputWindowLogger.LogError("Failed to fetch schema", ex);
                }
                finally
                {
                    _isFetching = false;
                }
            });
        }
    }
}
