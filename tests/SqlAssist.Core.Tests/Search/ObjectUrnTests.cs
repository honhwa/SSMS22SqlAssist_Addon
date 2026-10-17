using SqlAssist.Core.Search;
using Xunit;

namespace SqlAssist.Core.Tests.Search
{
    public class ObjectUrnTests
    {
        private const string Srv = "Server[@Name='.\\SQLEXPRESS']";

        [Fact]
        public void ServerQuotesName() => Assert.Equal(Srv, ObjectUrn.Server(".\\SQLEXPRESS"));

        [Fact]
        public void Procedure() =>
            Assert.Equal(new[] { Srv + "/Database[@Name='Shop']/StoredProcedure[@Name='GetOrders' and @Schema='dbo']" },
                ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "GetOrders", SearchObjectType.Procedure)));

        [Fact]
        public void SchemaAndQuotesDoubled() =>
            Assert.Equal(new[] { Srv + "/Database[@Name='O''Neil']/Schema[@Name='Sales']" },
                ObjectUrn.Candidates(Srv, new SearchItem("O'Neil", null, "Sales", SearchObjectType.Schema)));

        [Fact]
        public void ColumnOfKnownView() =>
            Assert.Equal(new[] { Srv + "/Database[@Name='Shop']/View[@Name='vOrders' and @Schema='dbo']/Column[@Name='Id']" },
                ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "Id", SearchObjectType.Column, "vOrders", parentType: SearchObjectType.View)));

        [Fact]
        public void TriggerOfUnknownOwner_TriesTableThenView()
        {
            var urns = ObjectUrn.Candidates(Srv, new SearchItem("Shop", "dbo", "trAudit", SearchObjectType.Trigger, "Orders"));
            Assert.Equal(2, urns.Count);
            Assert.EndsWith("/Table[@Name='Orders' and @Schema='dbo']/Trigger[@Name='trAudit']", urns[0]);
            Assert.EndsWith("/View[@Name='Orders' and @Schema='dbo']/Trigger[@Name='trAudit']", urns[1]);
        }

        [Fact]
        public void OwnerStripsLastStep() =>
            Assert.Equal(Srv + "/Database[@Name='Shop']/Table[@Name='a/b]' and @Schema='dbo']",
                ObjectUrn.Owner(Srv + "/Database[@Name='Shop']/Table[@Name='a/b]' and @Schema='dbo']/Column[@Name='x']"));
    }
}
