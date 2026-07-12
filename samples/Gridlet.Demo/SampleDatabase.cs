using Microsoft.Data.Sqlite;

namespace Gridlet.Demo;

/// <summary>Creates and seeds the SQLite demo database (idempotent, runs at startup).</summary>
public static class SampleDatabase
{
    public static async Task EnsureAsync(
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var check = connection.CreateCommand();
        check.CommandText =
            "SELECT COUNT(*) FROM main.sqlite_schema WHERE type = 'table' AND name = 'Customers';";
        if (Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)) > 0)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = SupplementalObjectsSql;
            await update.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation("SQLite sample database already exists — ensured current demo objects.");
            return;
        }

        logger.LogInformation("Creating and seeding SQLite sample database…");
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = SeedSql;
        command.CommandTimeout = 60;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("SQLite sample database created and seeded.");
    }

    private const string SeedSql =
        """
        CREATE TABLE Customers (
            CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
            FirstName TEXT NOT NULL,
            LastName TEXT NOT NULL,
            Email TEXT NOT NULL,
            Country TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
        );
        CREATE UNIQUE INDEX UX_Customers_Email ON Customers (Email);

        CREATE TABLE CustomerAudit (
            CustomerAuditId INTEGER PRIMARY KEY AUTOINCREMENT,
            CustomerId INTEGER NOT NULL,
            Action TEXT NOT NULL,
            ChangedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
        );

        CREATE TRIGGER AuditCustomerInsert
        AFTER INSERT ON Customers
        BEGIN
            INSERT INTO CustomerAudit (CustomerId, Action) VALUES (NEW.CustomerId, 'INSERT');
        END;

        CREATE TABLE Products (
            ProductId INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Category TEXT NOT NULL,
            UnitPrice NUMERIC NOT NULL,
            IsDiscontinued INTEGER NOT NULL DEFAULT (0)
        );

        CREATE TABLE Orders (
            OrderId INTEGER PRIMARY KEY AUTOINCREMENT,
            CustomerId INTEGER NOT NULL,
            OrderedAtUtc TEXT NOT NULL,
            Status TEXT NOT NULL,
            CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId)
                REFERENCES Customers (CustomerId)
        );
        CREATE INDEX IX_Orders_CustomerId ON Orders (CustomerId);

        CREATE TABLE OrderLines (
            OrderLineId INTEGER PRIMARY KEY AUTOINCREMENT,
            OrderId INTEGER NOT NULL,
            ProductId INTEGER NOT NULL,
            Quantity INTEGER NOT NULL,
            UnitPrice NUMERIC NOT NULL,
            CONSTRAINT FK_OrderLines_Orders FOREIGN KEY (OrderId)
                REFERENCES Orders (OrderId),
            CONSTRAINT FK_OrderLines_Products FOREIGN KEY (ProductId)
                REFERENCES Products (ProductId)
        );
        CREATE INDEX IX_OrderLines_OrderId ON OrderLines (OrderId);

        WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i + 1 FROM n WHERE i < 60)
        INSERT INTO Customers (FirstName, LastName, Email, Country, CreatedAtUtc)
        SELECT
            CASE i % 10 WHEN 0 THEN 'Ada' WHEN 1 THEN 'Grace' WHEN 2 THEN 'Alan'
                WHEN 3 THEN 'Edsger' WHEN 4 THEN 'Barbara' WHEN 5 THEN 'Donald'
                WHEN 6 THEN 'Linus' WHEN 7 THEN 'Margaret' WHEN 8 THEN 'Dennis' ELSE 'Ken' END,
            CASE i % 8 WHEN 0 THEN 'Lovelace' WHEN 1 THEN 'Hopper' WHEN 2 THEN 'Turing'
                WHEN 3 THEN 'Dijkstra' WHEN 4 THEN 'Liskov' WHEN 5 THEN 'Knuth'
                WHEN 6 THEN 'Torvalds' ELSE 'Hamilton' END,
            'user' || i || '@example.com',
            CASE i % 6 WHEN 0 THEN 'United Kingdom' WHEN 1 THEN 'Poland' WHEN 2 THEN 'Germany'
                WHEN 3 THEN 'United States' WHEN 4 THEN 'Norway' ELSE 'Japan' END,
            strftime('%Y-%m-%dT%H:%M:%fZ', 'now', printf('-%d days', i * 3))
        FROM n;

        WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i + 1 FROM n WHERE i < 20)
        INSERT INTO Products (Name, Category, UnitPrice, IsDiscontinued)
        SELECT
            CASE i % 5 WHEN 0 THEN 'Widget' WHEN 1 THEN 'Gadget' WHEN 2 THEN 'Sprocket'
                WHEN 3 THEN 'Gizmo' ELSE 'Doohickey' END || ' Mk' || i,
            CASE i % 4 WHEN 0 THEN 'Hardware' WHEN 1 THEN 'Tools'
                WHEN 2 THEN 'Accessories' ELSE 'Spares' END,
            round(2.50 + i * 3.25, 2),
            CASE WHEN i % 9 = 0 THEN 1 ELSE 0 END
        FROM n;

        WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i + 1 FROM n WHERE i < 300)
        INSERT INTO Orders (CustomerId, OrderedAtUtc, Status)
        SELECT
            (i - 1) % 60 + 1,
            strftime('%Y-%m-%dT%H:%M:%fZ', 'now', printf('-%d hours', i * 7)),
            CASE i % 4 WHEN 0 THEN 'Pending' WHEN 1 THEN 'Shipped'
                WHEN 2 THEN 'Delivered' ELSE 'Cancelled' END
        FROM n;

        WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i + 1 FROM n WHERE i < 900)
        INSERT INTO OrderLines (OrderId, ProductId, Quantity, UnitPrice)
        SELECT (i - 1) % 300 + 1, (i - 1) % 20 + 1, (i - 1) % 5 + 1,
               round(4.99 + (i % 40), 2)
        FROM n;

        CREATE VIEW vw_OrderSummary AS
        SELECT o.OrderId,
               c.FirstName || ' ' || c.LastName AS CustomerName,
               o.OrderedAtUtc,
               o.Status,
               round(SUM(ol.Quantity * ol.UnitPrice), 2) AS TotalAmount,
               COUNT(*) AS LineCount
        FROM Orders o
        JOIN Customers c ON c.CustomerId = o.CustomerId
        JOIN OrderLines ol ON ol.OrderId = o.OrderId
        GROUP BY o.OrderId, c.FirstName, c.LastName, o.OrderedAtUtc, o.Status;
        """;

    private const string SupplementalObjectsSql =
        """
        CREATE TABLE IF NOT EXISTS CustomerAudit (
            CustomerAuditId INTEGER PRIMARY KEY AUTOINCREMENT,
            CustomerId INTEGER NOT NULL,
            Action TEXT NOT NULL,
            ChangedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
        );
        CREATE TRIGGER IF NOT EXISTS AuditCustomerInsert
        AFTER INSERT ON Customers
        BEGIN
            INSERT INTO CustomerAudit (CustomerId, Action) VALUES (NEW.CustomerId, 'INSERT');
        END;
        """;
}
