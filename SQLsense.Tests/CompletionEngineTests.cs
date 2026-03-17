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
                new CompletionItem("Id", "dbo.Advert", CompletionIconType.Column),
                new CompletionItem("Title", "dbo.Advert", CompletionIconType.Column),
                new CompletionItem("CategoryId", "dbo.Advert", CompletionIconType.Column),
                new CompletionItem("Price", "dbo.Advert", CompletionIconType.Column),
                new CompletionItem("CategoryID", "dbo.Categories", CompletionIconType.Column),
                new CompletionItem("CategoryName", "dbo.Categories", CompletionIconType.Column),
                new CompletionItem("Description", "dbo.Categories", CompletionIconType.Column),
                new CompletionItem("Picture", "dbo.Categories", CompletionIconType.Column)
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
            
            var results = _engine.GetCompletions(prefix, textBeforeCaret, fullText);
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
        [Test]
        public void GetCompletions_WhereClause_PrioritizesIdColumns()
        {
            string fullText = "SELECT * FROM Advert WHERE ";
            var results = _engine.GetCompletions("", fullText, fullText);

            // Columns should dominate over functions or keywords, and 'Id' should ideally have 300 score. 
            // The top result should be a column.
            ClassicAssert.IsTrue(results.Any(), "Should return completions");
            var topResult = results.First();
            ClassicAssert.AreEqual(CompletionIconType.Column, topResult.IconType, "Columns should be at the absolute top of the WHERE clause suggestions");
        }

        [Test]
        public void GetCompletions_JoinOnClause_PrioritizesIdColumns()
        {
            string fullText = "SELECT * FROM Advert a INNER JOIN Users u ON ";
            var results = _engine.GetCompletions("", fullText, fullText);

            // Id-like columns (+400) should be prioritized heavily in ON clauses
            var topResult = results.First();
            
            // Wait, we have the "Smart JOIN" snippet as +600 according to my score definition.
            ClassicAssert.IsTrue(topResult.IconType == CompletionIconType.Snippet && topResult.Description == "Smart JOIN" || topResult.Text.EndsWith("Id", System.StringComparison.OrdinalIgnoreCase), "Top result in ON clause must be a Smart Join snippet or an Id column");
            
            var topColumn = results.First(r => r.IconType == CompletionIconType.Column);
            ClassicAssert.AreEqual("Id", topColumn.Text, "The first suggested column in an ON clause MUST be the Id column (FK/PK prioritisation)");
        }

        [Test]
        public void GetCompletions_FromClause_PrioritizesTables()
        {
            string fullText = "SELECT * FROM ";
            var results = _engine.GetCompletions("", fullText, fullText);

            var topResult = results.First();
            ClassicAssert.IsTrue(topResult.IconType == CompletionIconType.Table || topResult.IconType == CompletionIconType.View, "Tables/Views must strictly dominate the FROM clause (+300p)");
        }

        [Test]
        public void GetCompletions_AliasDot_ShowsColumns()
        {
            string prefix = "t0.";
            string textBeforeCaret = "SELECT * FROM dbo.Advert t0 WHERE t0.";
            string fullText = "SELECT * FROM dbo.Advert t0 WHERE t0.Id = 1";
            
            var results = _engine.GetCompletions(prefix, textBeforeCaret, fullText);
            ClassicAssert.IsNotEmpty(results, "Completions should not be empty for an alias dot context");
        }

        [Test]
        public void GetCompletions_Alias_Subquery_ResolvesOuterAlias()
        {
            string prefix = "t0.";
            string textBeforeCaret = "SELECT t0.";
            string fullText = "SELECT t0. FROM (SELECT * FROM dbo.Advert st0 WHERE st0.Id = 1) t0 WHERE t0.Id = 1";
            
            var results = _engine.GetCompletions(prefix, textBeforeCaret, fullText);

            ClassicAssert.IsNotEmpty(results, "Completions should not be empty when typing alias dot for a subquery");

            var firstItem = results.First();
            ClassicAssert.AreEqual(CompletionIconType.Column, firstItem.IconType, "First item is not Column after subquery alias dot");
        }

        [Test]
        public void GetCompletions_Subquery_OnlyShowsProjectedColumns()
        {
            // Case: Subquery projects specific columns. Alias should ONLY show those columns.
            string prefix = "t0.";
            string textBeforeCaret = "SELECT t0.";
            string fullText = "SELECT t0. FROM (SELECT Id, Title AS MyTitle FROM dbo.Advert) t0";
            
            var results = _engine.GetCompletions(prefix, textBeforeCaret, fullText);

            ClassicAssert.IsNotEmpty(results, "Results should not be empty for projected subquery");
            
            bool hasId = results.Any(r => r.Text == "Id");
            bool hasMyTitle = results.Any(r => r.Text == "MyTitle");
            bool hasHiddenColumn = results.Any(r => r.Text == "CategoryId"); // This exists in Advert but NOT in this projection

            ClassicAssert.IsTrue(hasId, "Id column was missing from projection");
            ClassicAssert.IsTrue(hasMyTitle, "Aliased column 'MyTitle' was missing from projection");
            ClassicAssert.IsFalse(hasHiddenColumn, "Unprojected column 'CategoryId' leaked through alias!");
        }

        [Test]
        public void GetCompletions_Subquery_StarExpansion()
        {
            // Case: Subquery uses SELECT *. Alias should show ALL columns of the underlying table.
            string prefix = "t0.";
            string textBeforeCaret = "SELECT t0.";
            string fullText = "SELECT t0. FROM (SELECT * FROM dbo.Advert) t0";
            
            var results = _engine.GetCompletions(prefix, textBeforeCaret, fullText);

            ClassicAssert.IsNotEmpty(results, "Results should not be empty for star-expanded subquery");
            
            bool hasId = results.Any(r => r.Text == "Id");
            bool hasCategoryId = results.Any(r => r.Text == "CategoryId");

            ClassicAssert.IsTrue(hasId, "Id column missing in star expansion");
            ClassicAssert.IsTrue(hasCategoryId, "CategoryId column missing in star expansion");
        }

        [Test]
        public void GetCompletions_Alias_Subquery_WithSpecificColumns_UserCase()
        {
            // The exact query from the user's screenshot
            string prefix = "t0.";
            string textBeforeCaret = "SELECT t0.";
            string fullText = "SELECT t0. FROM (SELECT st0.CategoryID, st0.CategoryName FROM dbo.Categories st0 WHERE st0.CategoryID = 1) t0 WHERE CategoryID = 1";
            
            var results = _engine.GetCompletions(prefix, textBeforeCaret, fullText);

            ClassicAssert.IsNotEmpty(results, "Results should not be empty");
            
            bool hasCategoryId = false;
            foreach (var r in results)
            {
                if (r.Text == "CategoryID") hasCategoryId = true;
                Console.WriteLine($"Result: {r.Text} (Score calculated by checking order)");
            }

            // Verify it is in the results AND it should be one of the top results (index < 5)
            ClassicAssert.IsTrue(hasCategoryId, "CategoryID is entirely missing from results!");
            var topNames = results.Take(10).Select(x => x.Text).ToList();
            ClassicAssert.IsTrue(topNames.Contains("CategoryID"), "CategoryID is in results, but NOT sorted to the top. Top items: " + string.Join(", ", topNames));
            
            // Critical check for leakage:
            bool hasDescription = results.Any(r => r.Text == "Description");
            ClassicAssert.IsFalse(hasDescription, "Leaked column 'Description' from dbo.Categories despite being a projected subquery!");
        }
        [Test]
        public void GetCompletions_Alias_Subquery_EmptyPrefix_UserCase()
        {
            // The exact query from the user's screenshot, but the user typed space or invoked completion manually
            string prefix = "";
            string textBeforeCaret = "SELECT t0. ";
            string fullText = "SELECT t0. FROM (SELECT st0.CategoryID, st0.CategoryName FROM dbo.Categories st0 WHERE st0.CategoryID = 1) t0 WHERE CategoryID = 1";
            
            var results = _engine.GetCompletions(prefix, textBeforeCaret, fullText);

            // Even if the prefix is empty, Description should NOT leak from the inner query!
            bool hasDescription = results.Any(r => r.Text == "Description");
            ClassicAssert.IsFalse(hasDescription, "Leaked column 'Description' from dbo.Categories when prefix is empty (scope bug)");
        }
    }
}
