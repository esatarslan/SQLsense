using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SQLsense.Core
{
    public interface ISqlFormatter
    {
        string Format(string sql, out IList<ParseError> errors);
    }
}
