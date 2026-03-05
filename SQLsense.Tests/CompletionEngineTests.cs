using NUnit.Framework;
using SQLsense.Core.Completion;
using SQLsense.UI.Completion;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework.Legacy;

namespace SQLsense.Tests
{
    [TestFixture]
    public class CompletionEngineTests
    {
        private CompletionEngine _engine;

        [SetUp]
        public void Setup()
        {
            // Null SnippetManager is okay if we aren't testing snippet expansions currently
            _engine = new CompletionEngine(null);

            // Inject mock DB schema
            var mockObjects = new List<CompletionItem>
            {
                new CompletionItem("Advert", "Table", CompletionIconType.Table),
                new CompletionItem("Users", "Table", CompletionIconType.Table),
                new CompletionItem("dbo.Orders", "Table", CompletionIconType.Table)
            };
            
            var mockColumns = new List<CompletionItem>
            {
                new CompletionItem("Id", "int", CompletionIconType.Column) { Description = "Advert" },
                new CompletionItem("Title", "varchar", CompletionIconType.Column) { Description = "Advert" },
                new CompletionItem("Price", "decimal", CompletionIconType.Column) { Description = "Advert" }
            };

            DatabaseSchemaProvider.SetMockObjects(mockObjects);
            DatabaseSchemaProvider.SetMockColumns(mockColumns);
        }

        [Test]
        public void GetCompletions_UpdateStatement_ReturnsTableStartingWithPrefix()
        {
            // The exact bug: Typing "UPDATE A" completely suppressed the table list because A was seen as a finished word
            string textBeforeCaret = "UPDATE A";
            var results = _engine.GetCompletions("A", textBeforeCaret, textBeforeCaret);

            var tables = results.Where(r => r.IconType == CompletionIconType.Table).ToList();
            
            // Should return Advert because it starts with A
            ClassicAssert.IsTrue(tables.Any(t => t.Text == "Advert"), "Should contain Advert table");
            // Should NOT contain Users because it doesn't match A (Optional keyword logic might include it, but top result must be Advert)
            ClassicAssert.AreEqual("Advert", tables.First().Text, "Advert should be the top suggested table");
        }

        [Test]
        public void GetCompletions_SelectAliasDots_ReturnsColumnsForAlias()
        {
            // The other bug: WHERE t0. failed to return columns because the dot was stripped
            string fullText = "SELECT * FROM Advert t0 WHERE t0.";
            string textBeforeCaret = fullText;
            
            // Wait, CompletionEngine `GetCompletions(searchPrefix, fullText, localContextText)`
            // EditorCommandFilter handles the word splitting. We need to simulate the prefix that EditorCommandFilter yields.
            string prefix = "t0.";
            
            var results = _engine.GetCompletions(prefix, fullText, prefix);
            var columns = results.Where(r => r.IconType == CompletionIconType.Column).ToList();

            ClassicAssert.IsTrue(columns.Any(c => c.Text == "Title"), "Should return Advert columns");
            // Also ensure snippet expansion is properly formatted
            ClassicAssert.AreEqual("t0.Title", columns.First(c => c.Text == "Title").SnippetExpansion);
        }
        
        [Test]
        public void GetCompletions_UpdateStatementWithS_IncludesSet()
        {
            // Simulate the user typing "S" after "UPDATE Advert "
            string textBeforeCaret = "UPDATE Advert S";
            var results = _engine.GetCompletions("S", textBeforeCaret, textBeforeCaret);

            var containsSet = results.Any(r => r.IconType == CompletionIconType.Keyword && r.Text == "SET");
            ClassicAssert.IsTrue(containsSet, "Should suggest SET when typing 'S' after table name");
        }

        [Test]
        public void GetCompletions_UpdateSet_IncludesSetAndColumns()
        {
            string fullText = "UPDATE Advert SET ";
            var results = _engine.GetCompletions("", fullText, fullText);

            var keywords = results.Where(r => r.IconType == CompletionIconType.Keyword).ToList();
            var columns = results.Where(r => r.IconType == CompletionIconType.Column).ToList();

            // Set shouldn't be blindly added a second time if the user already typed SET.
            ClassicAssert.IsFalse(keywords.Any(k => k.Text == "SET"), "Should not suggest SET again since user just typed it");
            ClassicAssert.IsTrue(columns.Any(c => c.Text == "Title"), "Should suggest Advert columns");
        }
        [Test]
        public void GetCompletions_UpdateSet_WhereKeywordAfterAssignment()
        {
            // The user finished an assignment and is now typing the W for WHERE
            string fullText = "UPDATE Advert SET AdvertId = 1 w";
            var results = _engine.GetCompletions("w", fullText, fullText);

            var keywords = results.Where(r => r.IconType == CompletionIconType.Keyword).ToList();
            var columns = results.Where(r => r.IconType == CompletionIconType.Column).ToList();

            ClassicAssert.IsTrue(keywords.Any(k => k.Text == "WHERE"), "Should suggest keywords like WHERE after setting a value");
            ClassicAssert.IsFalse(columns.Any(), "Should NOT suggest columns as the assignment is over");
        }

        [Test]
        public void GetCompletions_UpdateSet_ColumnsAfterComma()
        {
            // The user typed a comma, meaning they want to set another column
            string fullText = "UPDATE Advert SET AdvertId = 1, ";
            var results = _engine.GetCompletions("", fullText, fullText);

            var columns = results.Where(r => r.IconType == CompletionIconType.Column).ToList();

            ClassicAssert.IsTrue(columns.Any(c => c.Text == "Title"), "Should suggest columns after a comma");
        }
        [Test]
        public void GetCompletions_SingleLetter_ShouldReturnKeywords()
        {
            var resultsU = _engine.GetCompletions("u", "u", "u");
            var resultsW = _engine.GetCompletions("w", "w", "w");

            var keywordsU = resultsU.Where(x => x.IconType == CompletionIconType.Keyword).ToList();
            var keywordsW = resultsW.Where(x => x.IconType == CompletionIconType.Keyword).ToList();

            TestContext.WriteLine($"Typed 'u': Total={resultsU.Count}, Keywords={keywordsU.Count}");
            TestContext.WriteLine($"Top 5 for 'u': {string.Join(", ", resultsU.Take(5).Select(x => x.Text))}");
            
            TestContext.WriteLine($"Typed 'w': Total={resultsW.Count}, Keywords={keywordsW.Count}");
            TestContext.WriteLine($"Top 5 for 'w': {string.Join(", ", resultsW.Take(5).Select(x => x.Text))}");

            ClassicAssert.IsTrue(resultsU.Any(x => x.Text == "UPDATE"), "Should suggest UPDATE when typing 'u'");
        }
    }
}
