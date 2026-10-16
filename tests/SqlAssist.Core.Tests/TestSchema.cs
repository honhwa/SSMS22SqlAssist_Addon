using SqlAssist.Core.Schema;

namespace SqlAssist.Core.Tests
{
    internal static class TestSchema
    {
        public static readonly DbObject Customers = Table("dbo", "Customers", "CustomerID", "Name", "Email");
        public static readonly DbObject Orders = Table("dbo", "Orders", "OrderID", "CustomerID", "OrderDate");
        public static readonly DbObject OrderDetails = Table("dbo", "OrderDetails", "OrderID", "LineNo", "ProductID", "Qty");
        public static readonly DbObject Products = Table("sales", "Products", "ProductID", "Name");
        public static readonly DbObject Employees = Table("dbo", "Employees", "EmployeeID", "ManagerID");
        public static readonly DbObject Shipments = Table("dbo", "Shipments", "ShipmentID", "OrderID", "LineNo", "Ongoing Qty");

        public static SchemaModel Build() => new SchemaModel(
            new[] { Customers, Orders, OrderDetails, Products, Employees, Shipments },
            new[]
            {
                new ForeignKey("FK_Orders_Customers", Orders, Customers, new[] { ("CustomerID", "CustomerID") }),
                new ForeignKey("FK_OrderDetails_Orders", OrderDetails, Orders, new[] { ("OrderID", "OrderID") }),
                new ForeignKey("FK_OrderDetails_Products", OrderDetails, Products, new[] { ("ProductID", "ProductID") }),
                new ForeignKey("FK_Employees_Manager", Employees, Employees, new[] { ("ManagerID", "EmployeeID") }),
                new ForeignKey("FK_Shipments_OrderDetails", Shipments, OrderDetails, new[] { ("OrderID", "OrderID"), ("LineNo", "LineNo") }),
            });

        private static DbObject Table(string schema, string name, params string[] columns)
        {
            var cols = new DbColumn[columns.Length];
            for (int i = 0; i < columns.Length; i++) cols[i] = new DbColumn(columns[i], "int");
            return new DbObject(schema, name, false, cols);
        }
    }
}
