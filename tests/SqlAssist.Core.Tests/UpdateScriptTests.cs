using System;
using System.Linq;
using SqlAssist.Core.Results;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class UpdateScriptTests
    {
        private static readonly ResultColumn[] Items =
        {
            new ResultColumn("Id", "int"),
            new ResultColumn("Code", "varchar(10)"),
            new ResultColumn("Name", "nvarchar(50)"),
            new ResultColumn("Qty", "int"),
        };

        private static readonly object[][] Rows =
        {
            new object[] { 1, "00123", "สวัสดี", 5 },
            new object[] { 2, "A-2", "it's", null },
        };

        private static UpdateScriptResult Build(string table, int[] set, int[] keys, object[][] rows = null, ResultColumn[] columns = null) =>
            UpdateScriptBuilder.Build(columns ?? Items, rows ?? Rows, new UpdateScriptOptions(table, set, keys), "\n");

        [Fact]
        public void BuildsOneUpdatePerRow()
        {
            var result = Build("dbo.Items", new[] { 2, 3 }, new[] { 0 });
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
            Assert.Equal(
                "SET DATEFORMAT ymd\n\n" +
                "UPDATE dbo.Items SET Name = N'สวัสดี', Qty = 5 WHERE Id = 1\nGO\n" +
                "UPDATE dbo.Items SET Name = N'it''s', Qty = NULL WHERE Id = 2\nGO\n",
                result.Script);
        }

        [Fact]
        public void MultipleKeysAreJoinedWithAnd()
        {
            var result = Build("dbo.Items", new[] { 3 }, new[] { 0, 1 });
            Assert.Contains("UPDATE dbo.Items SET Qty = 5 WHERE Id = 1 AND Code = '00123'\nGO\n", result.Script);
            Assert.Contains("UPDATE dbo.Items SET Qty = NULL WHERE Id = 2 AND Code = 'A-2'\nGO\n", result.Script);
        }

        [Fact]
        public void NullKeyBecomesIsNull()
        {
            var rows = new[] { new object[] { null, "x", "n", 1 }, new object[] { DBNull.Value, "y", "m", 2 } };
            var result = Build("t", new[] { 2 }, new[] { 0, 1 }, rows);
            Assert.Contains("UPDATE t SET Name = N'n' WHERE Id IS NULL AND Code = 'x'\nGO\n", result.Script);
            Assert.Contains("UPDATE t SET Name = N'm' WHERE Id IS NULL AND Code = 'y'\nGO\n", result.Script);
            Assert.DoesNotContain("= NULL WHERE", result.Script);
        }

        [Fact]
        public void KeyColumnIsOnlySetWhenTheCallerAsksForIt()
        {
            string onlyKey = Build("t", new[] { 3 }, new[] { 0 }).Script;
            Assert.DoesNotContain("SET Id", onlyKey);

            string both = Build("t", new[] { 0, 3 }, new[] { 0 }).Script;
            Assert.Contains("UPDATE t SET Id = 1, Qty = 5 WHERE Id = 1\nGO\n", both);
        }

        [Fact]
        public void KeepsValuesAsTheGridHoldsThem()
        {
            var columns = new[]
            {
                new ResultColumn("Id", "int"),
                new ResultColumn("At", "datetime"),
                new ResultColumn("Bin", "varbinary(8)"),
                new ResultColumn("Price", "decimal(10,2)"),
                new ResultColumn("Ref", "uniqueidentifier"),
            };
            var rows = new[]
            {
                new object[] { 7, new DateTime(2024, 1, 31, 13, 5, 9, 120), new byte[] { 0, 0xFF, 0x10 }, 12.50m, Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e") },
            };
            string script = Build("t", new[] { 1, 2, 3, 4 }, new[] { 0 }, rows, columns).Script;
            Assert.Contains("UPDATE t SET At = '2024-01-31 13:05:09.120', Bin = 0x00FF10, Price = 12.50, Ref = '0F8FAD5B-D9CB-469F-A165-70867728950E' WHERE Id = 7", script);
        }

        [Fact]
        public void QuotesColumnNamesAndTheTarget()
        {
            var columns = new[] { new ResultColumn("Order Id", "int"), new ResultColumn("", "int"), new ResultColumn("Select", "int") };
            var rows = new[] { new object[] { 1, 2, 3 } };
            var result = Build("Sales.[Order Line]", new[] { 1, 2 }, new[] { 0 }, rows, columns);
            Assert.Contains("UPDATE Sales.[Order Line] SET Column2 = 2, [Select] = 3 WHERE [Order Id] = 1", result.Script);
        }

        [Fact]
        public void NormalisesTheTargetName()
        {
            Assert.Contains("UPDATE dbo.[Order Line] SET", Build("[dbo].[Order Line]", new[] { 3 }, new[] { 0 }).Script);
        }

        [Fact]
        public void NoScriptWithoutAKey()
        {
            var result = Build("dbo.Items", new[] { 3 }, new int[0]);
            Assert.False(result.IsValid);
            Assert.Null(result.Script);
            Assert.Single(result.Errors);
        }

        [Fact]
        public void NoScriptWithoutASetColumn()
        {
            var result = Build("dbo.Items", new int[0], new[] { 0 });
            Assert.False(result.IsValid);
            Assert.Null(result.Script);
            Assert.Single(result.Errors);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("dbo.Items; DROP TABLE x")]
        [InlineData("a.b.c.d")]
        public void NoScriptForAnInvalidTarget(string table)
        {
            var result = Build(table, new[] { 3 }, new[] { 0 });
            Assert.False(result.IsValid);
            Assert.Null(result.Script);
            Assert.Single(result.Errors);
        }

        [Fact]
        public void ReportsEveryProblemAtOnce()
        {
            var result = Build("", new int[0], new int[0]);
            Assert.Equal(3, result.Errors.Count);
        }

        [Fact]
        public void NoScriptForAColumnTheResultSetDoesNotHave()
        {
            Assert.False(Build("t", new[] { 9 }, new[] { 0 }).IsValid);
            Assert.False(Build("t", new[] { 3 }, new[] { -1 }).IsValid);
        }

        [Fact]
        public void NoRowsNoScript()
        {
            var result = Build("t", new[] { 3 }, new[] { 0 }, new object[0][]);
            Assert.False(result.IsValid);
            Assert.Null(result.Script);
        }

        [Fact]
        public void FindsRowsThatShareAKey()
        {
            var rows = new[]
            {
                new object[] { 1, "a", "x", 1 },
                new object[] { 2, "b", "y", 2 },
                new object[] { 1, "c", "z", 3 },
                new object[] { 3, "d", "w", 4 },
                new object[] { 1, "e", "v", 5 },
                new object[] { 2, "f", "u", 6 },
            };
            var duplicates = UpdateScriptBuilder.FindDuplicateKeys(Items, rows, new[] { 0 });
            Assert.Equal(2, duplicates.Count);
            Assert.Equal("Id = 1", duplicates[0].Condition);
            Assert.Equal(new[] { 1, 3, 5 }, duplicates[0].Rows);
            Assert.Equal("Id = 2", duplicates[1].Condition);
            Assert.Equal(new[] { 2, 6 }, duplicates[1].Rows);
        }

        [Fact]
        public void NullKeysAreTheSameKey()
        {
            var rows = new[] { new object[] { null, "a", "x", 1 }, new object[] { DBNull.Value, "a", "y", 2 } };
            var duplicates = UpdateScriptBuilder.FindDuplicateKeys(Items, rows, new[] { 0, 1 });
            Assert.Single(duplicates);
            Assert.Equal("Id IS NULL AND Code = 'a'", duplicates[0].Condition);
        }

        [Fact]
        public void AllKeyColumnsMustMatchToBeADuplicate()
        {
            var rows = new[] { new object[] { 1, "a", "x", 1 }, new object[] { 1, "b", "x", 1 } };
            Assert.Empty(UpdateScriptBuilder.FindDuplicateKeys(Items, rows, new[] { 0, 1 }));
            Assert.Single(UpdateScriptBuilder.FindDuplicateKeys(Items, rows, new[] { 0 }));
        }

        [Fact]
        public void NoDuplicatesWithoutKeys()
        {
            Assert.Empty(UpdateScriptBuilder.FindDuplicateKeys(Items, Rows, new int[0]));
        }

        [Fact]
        public void DuplicateKeysDoNotStopTheScript()
        {
            var rows = new[] { new object[] { 1, "a", "x", 1 }, new object[] { 1, "b", "y", 2 } };
            var result = Build("t", new[] { 2 }, new[] { 0 }, rows);
            Assert.True(result.IsValid);
            Assert.Equal(2, result.Script.Split(new[] { "UPDATE t" }, StringSplitOptions.None).Length - 1);
        }

        [Fact]
        public void UpdateKindIsNoLongerBuiltFromAGuessedKey()
        {
            Assert.Throws<ArgumentException>(() =>
                ResultScriptBuilder.Build(ResultScriptKind.Update, Items, new[] { 3 }, Rows, "\n"));
        }
    }
}
