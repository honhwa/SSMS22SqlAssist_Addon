using SqlAssist.Core.Schema;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class ConnectionKeyTests
    {
        [Fact]
        public void Windows_and_sql_keys_are_what_they_were_before()
        {
            Assert.Equal("srv1|db1|windows", ConnectionKey.ForDatabase("SRV1", "Db1", AuthKind.Windows, null));
            Assert.Equal("srv1|db1|sa", ConnectionKey.ForDatabase("SRV1", "Db1", AuthKind.Sql, "SA"));
            Assert.Equal("srv1||windows", ConnectionKey.ForDatabase("SRV1", null, AuthKind.Windows, null));
            Assert.Equal("srv1|windows", ConnectionKey.ForServer("SRV1", AuthKind.Windows, "ignored"));
            Assert.Equal("srv1|sa", ConnectionKey.ForServer("SRV1", AuthKind.Sql, "sa"));
            Assert.Equal("srv1|db1|", ConnectionKey.ForDatabase("SRV1", "Db1", AuthKind.Sql, null));
        }

        [Fact]
        public void Entra_never_shares_a_key_with_windows_or_another_account()
        {
            string a = ConnectionKey.ForDatabase("x.database.windows.net", "db", AuthKind.Entra, "A@corp.com");
            string b = ConnectionKey.ForDatabase("x.database.windows.net", "db", AuthKind.Entra, "b@corp.com");
            Assert.Equal("x.database.windows.net|db|entra:a@corp.com", a);
            Assert.NotEqual(a, b);
            Assert.NotEqual(a, ConnectionKey.ForDatabase("x.database.windows.net", "db", AuthKind.Windows, null));
            Assert.Equal("x.database.windows.net|entra:", ConnectionKey.ForServer("x.database.windows.net", AuthKind.Entra, null));
        }
    }
}
