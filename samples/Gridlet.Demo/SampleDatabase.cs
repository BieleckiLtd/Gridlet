using Microsoft.Data.SqlClient;

namespace Gridlet.Demo;

/// <summary>Creates and seeds the GridletSample database (idempotent, runs at startup).</summary>
public static class SampleDatabase
{
    private const string DatabaseName = "GridletSample";

    public static async Task EnsureAsync(string connectionString, ILogger logger, CancellationToken cancellationToken)
    {
        var masterConnectionString = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString;

        await using (var master = new SqlConnection(masterConnectionString))
        {
            await master.OpenAsync(cancellationToken);

            await using var check = master.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name;";
            check.Parameters.AddWithValue("@name", DatabaseName);
            if ((int)(await check.ExecuteScalarAsync(cancellationToken))! > 0)
            {
                logger.LogInformation("Sample database {Database} already exists — skipping seed.", DatabaseName);
                return;
            }

            logger.LogInformation("Creating sample database {Database}…", DatabaseName);
            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{DatabaseName}];";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var sampleConnectionString = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = DatabaseName,
        }.ConnectionString;

        await using var connection = new SqlConnection(sampleConnectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var batch in Batches)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.CommandTimeout = 60;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        logger.LogInformation("Sample database {Database} created and seeded.", DatabaseName);
    }

    // Each entry is one batch (CREATE VIEW/PROCEDURE/FUNCTION must start its own batch).
    private static readonly string[] Batches =
    [
        """
        CREATE TABLE dbo.Customers (
            CustomerId   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
            FirstName    nvarchar(50)  NOT NULL,
            LastName     nvarchar(50)  NOT NULL,
            Email        nvarchar(120) NOT NULL,
            Country      nvarchar(60)  NOT NULL,
            CreatedAtUtc datetime2     NOT NULL CONSTRAINT DF_Customers_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
        );

        CREATE UNIQUE INDEX UX_Customers_Email ON dbo.Customers (Email);

        CREATE TABLE dbo.Products (
            ProductId      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
            Name           nvarchar(100) NOT NULL,
            Category       nvarchar(50)  NOT NULL,
            UnitPrice      decimal(10,2) NOT NULL,
            IsDiscontinued bit           NOT NULL CONSTRAINT DF_Products_IsDiscontinued DEFAULT (0)
        );

        CREATE TABLE dbo.Orders (
            OrderId      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
            CustomerId   int          NOT NULL CONSTRAINT FK_Orders_Customers REFERENCES dbo.Customers (CustomerId),
            OrderedAtUtc datetime2    NOT NULL,
            Status       nvarchar(20) NOT NULL
        );

        CREATE INDEX IX_Orders_CustomerId ON dbo.Orders (CustomerId);

        CREATE TABLE dbo.OrderLines (
            OrderLineId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderLines PRIMARY KEY,
            OrderId     int           NOT NULL CONSTRAINT FK_OrderLines_Orders REFERENCES dbo.Orders (OrderId),
            ProductId   int           NOT NULL CONSTRAINT FK_OrderLines_Products REFERENCES dbo.Products (ProductId),
            Quantity    int           NOT NULL,
            UnitPrice   decimal(10,2) NOT NULL
        );

        CREATE INDEX IX_OrderLines_OrderId ON dbo.OrderLines (OrderId);
        """,
        """
        WITH n AS (
            SELECT TOP (60) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
            FROM sys.all_objects
        )
        INSERT dbo.Customers (FirstName, LastName, Email, Country, CreatedAtUtc)
        SELECT
            CHOOSE(i % 10 + 1, N'Ada', N'Grace', N'Alan', N'Edsger', N'Barbara', N'Donald', N'Linus', N'Margaret', N'Dennis', N'Ken'),
            CHOOSE(i % 8 + 1, N'Lovelace', N'Hopper', N'Turing', N'Dijkstra', N'Liskov', N'Knuth', N'Torvalds', N'Hamilton'),
            CONCAT(N'user', i, N'@example.com'),
            CHOOSE(i % 6 + 1, N'United Kingdom', N'Poland', N'Germany', N'United States', N'Norway', N'Japan'),
            DATEADD(DAY, -i * 3, SYSUTCDATETIME())
        FROM n;

        WITH n AS (
            SELECT TOP (20) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
            FROM sys.all_objects
        )
        INSERT dbo.Products (Name, Category, UnitPrice, IsDiscontinued)
        SELECT
            CONCAT(CHOOSE(i % 5 + 1, N'Widget', N'Gadget', N'Sprocket', N'Gizmo', N'Doohickey'), N' Mk', i),
            CHOOSE(i % 4 + 1, N'Hardware', N'Tools', N'Accessories', N'Spares'),
            CAST(2.50 + i * 3.25 AS decimal(10,2)),
            CASE WHEN i % 9 = 0 THEN 1 ELSE 0 END
        FROM n;

        WITH n AS (
            SELECT TOP (300) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
            FROM sys.all_objects a CROSS JOIN sys.all_objects b
        )
        INSERT dbo.Orders (CustomerId, OrderedAtUtc, Status)
        SELECT
            i % 60 + 1,
            DATEADD(HOUR, -i * 7, SYSUTCDATETIME()),
            CHOOSE(i % 4 + 1, N'Pending', N'Shipped', N'Delivered', N'Cancelled')
        FROM n;

        WITH n AS (
            SELECT TOP (900) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
            FROM sys.all_objects a CROSS JOIN sys.all_objects b
        )
        INSERT dbo.OrderLines (OrderId, ProductId, Quantity, UnitPrice)
        SELECT
            i % 300 + 1,
            i % 20 + 1,
            i % 5 + 1,
            CAST(4.99 + (i % 40) AS decimal(10,2))
        FROM n;
        """,
        """
        CREATE VIEW dbo.vw_OrderSummary AS
        SELECT o.OrderId,
               c.FirstName + N' ' + c.LastName AS CustomerName,
               o.OrderedAtUtc,
               o.Status,
               SUM(ol.Quantity * ol.UnitPrice) AS TotalAmount,
               COUNT(*) AS LineCount
        FROM dbo.Orders o
        JOIN dbo.Customers c ON c.CustomerId = o.CustomerId
        JOIN dbo.OrderLines ol ON ol.OrderId = o.OrderId
        GROUP BY o.OrderId, c.FirstName, c.LastName, o.OrderedAtUtc, o.Status;
        """,
        """
        CREATE PROCEDURE dbo.usp_GetCustomerOrders
            @CustomerId int
        AS
        BEGIN
            SET NOCOUNT ON;

            SELECT o.OrderId, o.OrderedAtUtc, o.Status,
                   SUM(ol.Quantity * ol.UnitPrice) AS TotalAmount
            FROM dbo.Orders o
            JOIN dbo.OrderLines ol ON ol.OrderId = o.OrderId
            WHERE o.CustomerId = @CustomerId
            GROUP BY o.OrderId, o.OrderedAtUtc, o.Status
            ORDER BY o.OrderedAtUtc DESC;
        END
        """,
        """
        CREATE FUNCTION dbo.fn_OrderTotal (@OrderId int)
        RETURNS decimal(12,2)
        AS
        BEGIN
            RETURN (SELECT SUM(Quantity * UnitPrice) FROM dbo.OrderLines WHERE OrderId = @OrderId);
        END
        """,
    ];
}
