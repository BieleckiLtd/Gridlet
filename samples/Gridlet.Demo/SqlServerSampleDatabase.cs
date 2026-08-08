using Microsoft.Data.SqlClient;

namespace Gridlet.Demo;

/// <summary>Creates and seeds the SQL Server LocalDB demo database.</summary>
public static class SqlServerSampleDatabase
{
    public static async Task<bool> TryEnsureAsync(
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var target = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(target.InitialCatalog))
            {
                throw new InvalidOperationException(
                    "The SQL Server demo connection must specify a database.");
            }

            await EnsureDatabaseAsync(target, cancellationToken);
            await EnsureSchemaAsync(target.ConnectionString, logger, cancellationToken);
            return true;
        }
        catch (SqlException exception)
        {
            logger.LogWarning(
                exception,
                "SQL Server LocalDB could not be initialized; continuing with the SQLite demo only.");
            return false;
        }
    }

    private static async Task EnsureDatabaseAsync(
        SqlConnectionStringBuilder target,
        CancellationToken cancellationToken)
    {
        var database = target.InitialCatalog;
        var master = new SqlConnectionStringBuilder(target.ConnectionString)
        {
            InitialCatalog = "master",
        };

        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(@database) IS NULL CREATE DATABASE {QuoteIdentifier(database)};";
        command.Parameters.AddWithValue("@database", database);
        command.CommandTimeout = 60;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureSchemaAsync(
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT CASE WHEN OBJECT_ID(N'dbo.Customers', N'U') IS NULL THEN 0 ELSE 1 END;";
        var exists = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) == 1;

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        if (!exists)
        {
            logger.LogInformation("Creating and seeding SQL Server LocalDB sample database…");
            await ExecuteAsync(connection, transaction, SeedSql, cancellationToken);
        }

        foreach (var statement in SupplementalObjectSql)
        {
            await ExecuteAsync(connection, transaction, statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(exists
            ? "SQL Server LocalDB sample database already exists — ensured current demo objects."
            : "SQL Server LocalDB sample database created and seeded.");
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 60;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (identifier.Length > 128)
        {
            throw new InvalidOperationException("The SQL Server demo database name is too long.");
        }

        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private const string SeedSql =
        """
        IF SCHEMA_ID(N'catalog') IS NULL EXEC(N'CREATE SCHEMA catalog');
        IF SCHEMA_ID(N'sales') IS NULL EXEC(N'CREATE SCHEMA sales');

        CREATE TABLE dbo.Customers (
            CustomerId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
            FirstName nvarchar(100) NOT NULL,
            LastName nvarchar(100) NOT NULL,
            Email nvarchar(320) NOT NULL,
            Country nvarchar(100) NOT NULL,
            CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Customers_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
            CONSTRAINT UX_Customers_Email UNIQUE (Email)
        );

        CREATE TABLE dbo.CustomerAudit (
            CustomerAuditId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerAudit PRIMARY KEY,
            CustomerId int NOT NULL,
            Action nvarchar(20) NOT NULL,
            ChangedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_CustomerAudit_ChangedAtUtc DEFAULT SYSUTCDATETIME()
        );

        CREATE TABLE catalog.Products (
            ProductId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
            Name nvarchar(200) NOT NULL,
            Category nvarchar(100) NOT NULL,
            UnitPrice decimal(18,2) NOT NULL,
            IsDiscontinued bit NOT NULL CONSTRAINT DF_Products_IsDiscontinued DEFAULT 0
        );

        CREATE TABLE sales.Orders (
            OrderId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
            CustomerId int NOT NULL,
            OrderedAtUtc datetime2(3) NOT NULL,
            Status nvarchar(30) NOT NULL,
            CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (CustomerId)
        );
        CREATE INDEX IX_Orders_CustomerId ON sales.Orders (CustomerId);

        CREATE TABLE sales.OrderLines (
            OrderLineId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderLines PRIMARY KEY,
            OrderId int NOT NULL,
            ProductId int NOT NULL,
            Quantity int NOT NULL,
            UnitPrice decimal(18,2) NOT NULL,
            CONSTRAINT CK_OrderLines_Quantity CHECK (Quantity > 0),
            CONSTRAINT FK_OrderLines_Orders FOREIGN KEY (OrderId) REFERENCES sales.Orders (OrderId),
            CONSTRAINT FK_OrderLines_Products FOREIGN KEY (ProductId) REFERENCES catalog.Products (ProductId)
        );
        CREATE INDEX IX_OrderLines_OrderId ON sales.OrderLines (OrderId);

        DECLARE @i int = 1;
        WHILE @i <= 60
        BEGIN
            INSERT dbo.Customers (FirstName, LastName, Email, Country, CreatedAtUtc)
            VALUES (
                CASE @i % 10 WHEN 0 THEN N'Ada' WHEN 1 THEN N'Grace' WHEN 2 THEN N'Alan'
                    WHEN 3 THEN N'Edsger' WHEN 4 THEN N'Barbara' WHEN 5 THEN N'Donald'
                    WHEN 6 THEN N'Linus' WHEN 7 THEN N'Margaret' WHEN 8 THEN N'Dennis' ELSE N'Ken' END,
                CASE @i % 8 WHEN 0 THEN N'Lovelace' WHEN 1 THEN N'Hopper' WHEN 2 THEN N'Turing'
                    WHEN 3 THEN N'Dijkstra' WHEN 4 THEN N'Liskov' WHEN 5 THEN N'Knuth'
                    WHEN 6 THEN N'Torvalds' ELSE N'Hamilton' END,
                CONCAT(N'user', @i, N'@example.com'),
                CASE @i % 6 WHEN 0 THEN N'United Kingdom' WHEN 1 THEN N'Poland' WHEN 2 THEN N'Germany'
                    WHEN 3 THEN N'United States' WHEN 4 THEN N'Norway' ELSE N'Japan' END,
                DATEADD(day, -@i * 3, SYSUTCDATETIME()));
            SET @i += 1;
        END;

        SET @i = 1;
        WHILE @i <= 20
        BEGIN
            INSERT catalog.Products (Name, Category, UnitPrice, IsDiscontinued)
            VALUES (
                CONCAT(CASE @i % 5 WHEN 0 THEN N'Widget' WHEN 1 THEN N'Gadget'
                    WHEN 2 THEN N'Sprocket' WHEN 3 THEN N'Gizmo' ELSE N'Doohickey' END, N' Mk', @i),
                CASE @i % 4 WHEN 0 THEN N'Hardware' WHEN 1 THEN N'Tools'
                    WHEN 2 THEN N'Accessories' ELSE N'Spares' END,
                CAST(2.50 + @i * 3.25 AS decimal(18,2)),
                CASE WHEN @i % 9 = 0 THEN 1 ELSE 0 END);
            SET @i += 1;
        END;

        SET @i = 1;
        WHILE @i <= 300
        BEGIN
            INSERT sales.Orders (CustomerId, OrderedAtUtc, Status)
            VALUES ((@i - 1) % 60 + 1, DATEADD(hour, -@i * 7, SYSUTCDATETIME()),
                CASE @i % 4 WHEN 0 THEN N'Pending' WHEN 1 THEN N'Shipped'
                    WHEN 2 THEN N'Delivered' ELSE N'Cancelled' END);
            SET @i += 1;
        END;

        SET @i = 1;
        WHILE @i <= 900
        BEGIN
            INSERT sales.OrderLines (OrderId, ProductId, Quantity, UnitPrice)
            VALUES ((@i - 1) % 300 + 1, (@i - 1) % 20 + 1, (@i - 1) % 5 + 1,
                CAST(4.99 + (@i % 40) AS decimal(18,2)));
            SET @i += 1;
        END;
        """;

    private static readonly string[] SupplementalObjectSql =
    [
        "IF SCHEMA_ID(N'catalog') IS NULL EXEC(N'CREATE SCHEMA catalog'); " +
        "IF SCHEMA_ID(N'sales') IS NULL EXEC(N'CREATE SCHEMA sales');",
        """
        CREATE OR ALTER VIEW sales.vw_OrderSummary AS
        SELECT o.OrderId,
               CONCAT(c.FirstName, N' ', c.LastName) AS CustomerName,
               o.OrderedAtUtc,
               o.Status,
               CAST(SUM(ol.Quantity * ol.UnitPrice) AS decimal(18,2)) AS TotalAmount,
               COUNT_BIG(*) AS LineCount
        FROM sales.Orders o
        JOIN dbo.Customers c ON c.CustomerId = o.CustomerId
        JOIN sales.OrderLines ol ON ol.OrderId = o.OrderId
        GROUP BY o.OrderId, c.FirstName, c.LastName, o.OrderedAtUtc, o.Status;
        """,
        """
        CREATE OR ALTER PROCEDURE sales.GetCustomerOrders
            @CustomerId int
        AS
        BEGIN
            SET NOCOUNT ON;
            SELECT o.OrderId, o.OrderedAtUtc, o.Status,
                   CAST(SUM(ol.Quantity * ol.UnitPrice) AS decimal(18,2)) AS TotalAmount
            FROM sales.Orders o
            JOIN sales.OrderLines ol ON ol.OrderId = o.OrderId
            WHERE o.CustomerId = @CustomerId
            GROUP BY o.OrderId, o.OrderedAtUtc, o.Status
            ORDER BY o.OrderedAtUtc DESC;
        END;
        """,
        """
        CREATE OR ALTER FUNCTION sales.OrderTotal (@OrderId int)
        RETURNS decimal(18,2)
        AS
        BEGIN
            DECLARE @total decimal(18,2);
            SELECT @total = SUM(Quantity * UnitPrice)
            FROM sales.OrderLines
            WHERE OrderId = @OrderId;
            RETURN COALESCE(@total, 0);
        END;
        """,
        """
        CREATE OR ALTER TRIGGER dbo.AuditCustomerInsert
        ON dbo.Customers
        AFTER INSERT
        AS
        BEGIN
            SET NOCOUNT ON;
            INSERT dbo.CustomerAudit (CustomerId, Action)
            SELECT CustomerId, N'INSERT' FROM inserted;
        END;
        """,
    ];
}
