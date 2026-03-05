using System;
using NUnit.Framework;
using SQLsense.Core.Analysis;

namespace SQLsense.Tests
{
    [TestFixture]
    public class ContextStateAnalyzerTests
    {
        [TestCase("SELECT ", SqlContextState.SelectColumns, "Basic SELECT waiting for columns")]
        [TestCase("SELECT t0.[caret] FROM dbo.Users t0", SqlContextState.SelectColumns, "SELECT mid-alias typing")]
        [TestCase("SELECT Id, Name, ", SqlContextState.SelectColumns, "SELECT multiple columns")]
        [TestCase("SELECT DISTINCT ", SqlContextState.SelectColumns, "SELECT DISTINCT variant")]
        [TestCase("SELECT TOP 10 ", SqlContextState.SelectColumns, "SELECT TOP variant")]
        public void DetermineState_SelectQueries_ReturnsSelectColumns(string sql, SqlContextState expectedState, string description)
        {
            AssertState(sql, expectedState, description);
        }

        [TestCase("UPDATE ", SqlContextState.UpdateTable, "Basic UPDATE waiting for table")]
        [TestCase("UPDATE dbo.", SqlContextState.UpdateTable, "UPDATE typing schema")]
        [TestCase("UPDATE dbo.Advert ", SqlContextState.UpdateSetColumns, "UPDATE table finished, expecting SET")]
        [TestCase("UPDATE Advert SET ", SqlContextState.UpdateSetColumns, "UPDATE SET expecting columns")]
        [TestCase("UPDATE Advert \nSET ", SqlContextState.UpdateSetColumns, "UPDATE SET across newlines")]
        public void DetermineState_UpdateQueries_ReturnsCorrectState(string sql, SqlContextState expectedState, string description)
        {
            AssertState(sql, expectedState, description);
        }

        [TestCase("SELECT * FROM Users WHERE Id = ", SqlContextState.Unknown, "WHERE equatable assignment blocks tables/columns")]
        [TestCase("UPDATE Users SET Name = ", SqlContextState.Unknown, "SET equatable assignment blocks tables/columns")]
        [TestCase("SELECT * FROM Users WHERE CreatedAt > ", SqlContextState.Unknown, "WHERE greater than assignment")]
        [TestCase("SELECT * FROM Users WHERE CreatedAt >= ", SqlContextState.Unknown, "WHERE greater eq assignment")]
        [TestCase("SELECT * FROM Users WHERE Id <> ", SqlContextState.Unknown, "WHERE not eq assignment")]
        [TestCase("SELECT * FROM Users WHERE ", SqlContextState.WhereCondition, "WHERE standalone expecting columns")]
        [TestCase("SELECT * FROM Users WHERE t0.", SqlContextState.WhereCondition, "WHERE typing alias")]
        public void DetermineState_Assignments_ReturnsUnknown(string sql, SqlContextState expectedState, string description)
        {
            AssertState(sql, expectedState, description);
        }

        [TestCase("SELECT * FROM ", SqlContextState.FromTables, "FROM expecting tables")]
        [TestCase("SELECT * FROM dbo.Users u INNER JOIN ", SqlContextState.JoinTables, "JOIN expecting tables")]
        [TestCase("SELECT * FROM dbo.Users u LEFT OUTER JOIN ", SqlContextState.JoinTables, "LEFT OUTER JOIN expecting tables")]
        [TestCase("SELECT * FROM dbo.Users u INNER JOIN dbo.Orders o ON ", SqlContextState.JoinOnCondition, "ON expecting columns")]
        public void DetermineState_JoinsAndFroms_ReturnsCorrectState(string sql, SqlContextState expectedState, string description)
        {
            AssertState(sql, expectedState, description);
        }

        [TestCase("INSERT INTO ", SqlContextState.InsertTable, "INSERT INTO expecting tables")]
        [TestCase("SELECT * FROM Users GROUP BY ", SqlContextState.GroupByColumns, "GROUP BY expecting columns")]
        [TestCase("SELECT * FROM Users ORDER BY ", SqlContextState.OrderByColumns, "ORDER BY expecting columns")]
        public void DetermineState_OtherClauses_ReturnsCorrectState(string sql, SqlContextState expectedState, string description)
        {
            AssertState(sql, expectedState, description);
        }

        private void AssertState(string sql, SqlContextState expectedState, string description)
        {
            int caretPosition;
            if (sql.Contains("[caret]"))
            {
                caretPosition = sql.IndexOf("[caret]");
                sql = sql.Replace("[caret]", "");
            }
            else
            {
                caretPosition = sql.Length;
            }
            
            var actualState = ContextStateAnalyzer.DetermineState(sql, caretPosition);
            
            Assert.That(actualState, Is.EqualTo(expectedState), 
                $"Failed scenario: '{description}'\nExpected: {expectedState}, but got: {actualState}\nSQL: '{sql}'");
        }
    }
}
