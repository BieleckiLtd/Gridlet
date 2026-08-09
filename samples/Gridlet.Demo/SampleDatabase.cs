using Microsoft.Data.Sqlite;

namespace Gridlet.Demo;

/// <summary>Creates and seeds the Byte Pizza SQLite demo database (idempotent, runs at startup).</summary>
public static class SampleDatabase
{
    private const int SchemaVersion = 1;

    public static async Task EnsureAsync(
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var check = connection.CreateCommand();
        check.CommandText =
            "SELECT SchemaVersion FROM BytePizzaMetadata WHERE MetadataId = 1 AND EXISTS " +
            "(SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'BytePizzaMetadata');";

        try
        {
            var version = await check.ExecuteScalarAsync(cancellationToken);
            if (version is not null && Convert.ToInt32(version) == SchemaVersion)
            {
                logger.LogInformation("Byte Pizza sample database already exists and is current.");
                return;
            }
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            // A brand-new database does not contain BytePizzaMetadata yet.
        }

        await using var objectCheck = connection.CreateCommand();
        objectCheck.CommandText =
            "SELECT COUNT(*) FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%';";
        if (Convert.ToInt64(await objectCheck.ExecuteScalarAsync(cancellationToken)) > 0)
        {
            throw new InvalidOperationException(
                "The configured SQLite database is not an empty Byte Pizza database. " +
                "Choose an empty database or remove the existing demo database before starting Gridlet.Demo.");
        }

        logger.LogInformation("Creating and seeding the Byte Pizza SQLite sample database...");
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = SeedSql;
        command.CommandTimeout = 60;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Byte Pizza SQLite sample database created and seeded.");
    }

    private const string SeedSql =
        """
        CREATE TABLE BytePizzaMetadata (
            MetadataId INTEGER PRIMARY KEY CHECK (MetadataId = 1),
            SchemaVersion INTEGER NOT NULL,
            RestaurantName TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
        ) STRICT;

        CREATE TABLE PizzaSizes (
            PizzaSizeId INTEGER PRIMARY KEY,
            Name TEXT NOT NULL UNIQUE,
            DiameterInches INTEGER NOT NULL CHECK (DiameterInches BETWEEN 6 AND 24),
            PriceAdjustmentPence INTEGER NOT NULL DEFAULT 0,
            CalorieMultiplier REAL NOT NULL CHECK (CalorieMultiplier > 0),
            SortOrder INTEGER NOT NULL UNIQUE
        ) STRICT;

        CREATE TABLE Pizzas (
            PizzaId INTEGER PRIMARY KEY,
            Name TEXT NOT NULL UNIQUE,
            Description TEXT NOT NULL,
            BasePricePence INTEGER NOT NULL CHECK (BasePricePence > 0),
            BaseCalories INTEGER NOT NULL CHECK (BaseCalories > 0),
            HeatLevel INTEGER NOT NULL DEFAULT 0 CHECK (HeatLevel BETWEEN 0 AND 5),
            IsVegetarian INTEGER NOT NULL DEFAULT 0 CHECK (IsVegetarian IN (0, 1)),
            IsVegan INTEGER NOT NULL DEFAULT 0 CHECK (IsVegan IN (0, 1)),
            IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
            DietaryMetadata TEXT NOT NULL DEFAULT '{}'
                CHECK (json_valid(DietaryMetadata)),
            Thumbnail BLOB NULL,
            CreatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
        ) STRICT;

        CREATE TABLE Toppings (
            ToppingId INTEGER PRIMARY KEY,
            Name TEXT NOT NULL UNIQUE,
            Category TEXT NOT NULL CHECK (Category IN ('Cheese', 'Meat', 'Seafood', 'Vegetable', 'Fruit', 'Sauce', 'Herb')),
            ExtraPricePence INTEGER NOT NULL DEFAULT 0 CHECK (ExtraPricePence >= 0),
            Calories INTEGER NOT NULL DEFAULT 0 CHECK (Calories >= 0),
            IsVegetarian INTEGER NOT NULL CHECK (IsVegetarian IN (0, 1)),
            IsVegan INTEGER NOT NULL CHECK (IsVegan IN (0, 1)),
            Allergens TEXT NOT NULL DEFAULT '[]' CHECK (json_valid(Allergens)),
            IsAvailable INTEGER NOT NULL DEFAULT 1 CHECK (IsAvailable IN (0, 1))
        ) STRICT;

        CREATE TABLE PizzaToppings (
            PizzaId INTEGER NOT NULL,
            ToppingId INTEGER NOT NULL,
            Portion REAL NOT NULL DEFAULT 1.0 CHECK (Portion > 0 AND Portion <= 3),
            IsRemovable INTEGER NOT NULL DEFAULT 1 CHECK (IsRemovable IN (0, 1)),
            PRIMARY KEY (PizzaId, ToppingId),
            FOREIGN KEY (PizzaId) REFERENCES Pizzas (PizzaId) ON DELETE CASCADE,
            FOREIGN KEY (ToppingId) REFERENCES Toppings (ToppingId) ON DELETE RESTRICT
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE Customers (
            CustomerId INTEGER PRIMARY KEY,
            FirstName TEXT NOT NULL,
            LastName TEXT NOT NULL,
            Email TEXT NOT NULL,
            Phone TEXT NULL,
            LoyaltyPoints INTEGER NOT NULL DEFAULT 0 CHECK (LoyaltyPoints >= 0),
            MarketingOptIn INTEGER NOT NULL DEFAULT 0 CHECK (MarketingOptIn IN (0, 1)),
            CreatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
        ) STRICT;

        CREATE TABLE Addresses (
            AddressId INTEGER PRIMARY KEY,
            CustomerId INTEGER NOT NULL,
            Label TEXT NOT NULL DEFAULT 'Home',
            Line1 TEXT NOT NULL,
            Line2 TEXT NULL,
            City TEXT NOT NULL,
            Postcode TEXT NOT NULL,
            DeliveryZone TEXT NOT NULL CHECK (DeliveryZone IN ('Central', 'North', 'East', 'South', 'West')),
            Latitude REAL NULL,
            Longitude REAL NULL,
            IsDefault INTEGER NOT NULL DEFAULT 0 CHECK (IsDefault IN (0, 1)),
            UNIQUE (CustomerId, Label),
            FOREIGN KEY (CustomerId) REFERENCES Customers (CustomerId) ON DELETE CASCADE
        ) STRICT;

        CREATE TABLE Drivers (
            DriverId INTEGER PRIMARY KEY,
            FirstName TEXT NOT NULL,
            LastName TEXT NOT NULL,
            Phone TEXT NOT NULL UNIQUE,
            VehicleType TEXT NOT NULL CHECK (VehicleType IN ('Bicycle', 'E-bike', 'Scooter', 'Car')),
            HireDate TEXT NOT NULL,
            IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1))
        ) STRICT;

        CREATE TABLE Promotions (
            PromotionId INTEGER PRIMARY KEY,
            Code TEXT NOT NULL UNIQUE,
            Description TEXT NOT NULL,
            DiscountType TEXT NOT NULL CHECK (DiscountType IN ('Percent', 'Fixed')),
            DiscountValue INTEGER NOT NULL CHECK (DiscountValue > 0),
            MinimumOrderPence INTEGER NOT NULL DEFAULT 0 CHECK (MinimumOrderPence >= 0),
            ValidFromUtc TEXT NOT NULL,
            ValidUntilUtc TEXT NOT NULL,
            MaxUses INTEGER NULL CHECK (MaxUses IS NULL OR MaxUses > 0),
            ParentPromotionId INTEGER NULL,
            IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
            CHECK (ValidUntilUtc > ValidFromUtc),
            FOREIGN KEY (ParentPromotionId) REFERENCES Promotions (PromotionId) ON DELETE SET NULL
        ) STRICT;

        CREATE TABLE Orders (
            OrderId INTEGER PRIMARY KEY,
            CustomerId INTEGER NOT NULL,
            PromotionId INTEGER NULL,
            OrderType TEXT NOT NULL CHECK (OrderType IN ('Delivery', 'Collection')),
            Status TEXT NOT NULL DEFAULT 'Placed'
                CHECK (Status IN ('Placed', 'Preparing', 'Baking', 'Ready', 'OutForDelivery', 'Delivered', 'Cancelled')),
            OrderedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
            RequestedForUtc TEXT NULL,
            UpdatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
            PaymentMetadata TEXT NOT NULL DEFAULT '{"source":"web","payment":"card","contactless":false}'
                CHECK (json_valid(PaymentMetadata)),
            TipPence INTEGER NOT NULL DEFAULT 0 CHECK (TipPence >= 0),
            Notes TEXT NULL,
            FOREIGN KEY (CustomerId) REFERENCES Customers (CustomerId) ON DELETE RESTRICT,
            FOREIGN KEY (PromotionId) REFERENCES Promotions (PromotionId) ON DELETE SET NULL
        ) STRICT;

        CREATE TABLE OrderItems (
            OrderItemId INTEGER PRIMARY KEY,
            OrderId INTEGER NOT NULL,
            PizzaId INTEGER NOT NULL,
            PizzaSizeId INTEGER NOT NULL,
            Quantity INTEGER NOT NULL DEFAULT 1 CHECK (Quantity > 0),
            UnitPricePence INTEGER NOT NULL CHECK (UnitPricePence > 0),
            CustomizationPricePence INTEGER NOT NULL DEFAULT 0,
            DiscountPence INTEGER NOT NULL DEFAULT 0 CHECK (DiscountPence >= 0),
            LineTotalPence INTEGER GENERATED ALWAYS AS
                (Quantity * (UnitPricePence + CustomizationPricePence) - DiscountPence) STORED,
            SpecialRequest TEXT NULL,
            CHECK (Quantity * (UnitPricePence + CustomizationPricePence) >= DiscountPence),
            FOREIGN KEY (OrderId) REFERENCES Orders (OrderId) ON DELETE CASCADE,
            FOREIGN KEY (PizzaId) REFERENCES Pizzas (PizzaId) ON DELETE RESTRICT,
            FOREIGN KEY (PizzaSizeId) REFERENCES PizzaSizes (PizzaSizeId) ON DELETE RESTRICT
        ) STRICT;

        CREATE TABLE OrderItemToppings (
            OrderItemId INTEGER NOT NULL,
            ToppingId INTEGER NOT NULL,
            Action TEXT NOT NULL CHECK (Action IN ('Add', 'Remove')),
            PriceAdjustmentPence INTEGER NOT NULL DEFAULT 0 CHECK (PriceAdjustmentPence >= 0),
            PRIMARY KEY (OrderItemId, ToppingId, Action),
            FOREIGN KEY (OrderItemId) REFERENCES OrderItems (OrderItemId) ON DELETE CASCADE,
            FOREIGN KEY (ToppingId) REFERENCES Toppings (ToppingId) ON DELETE RESTRICT
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE Deliveries (
            DeliveryId INTEGER PRIMARY KEY,
            OrderId INTEGER NOT NULL UNIQUE,
            AddressId INTEGER NOT NULL,
            DriverId INTEGER NULL,
            Status TEXT NOT NULL DEFAULT 'Pending'
                CHECK (Status IN ('Pending', 'Assigned', 'OutForDelivery', 'Delivered', 'Cancelled')),
            EstimatedArrivalUtc TEXT NULL,
            DispatchedAtUtc TEXT NULL,
            DeliveredAtUtc TEXT NULL,
            DistanceKm REAL NOT NULL CHECK (DistanceKm >= 0),
            DeliveryInstructions TEXT NULL CHECK (DeliveryInstructions IS NULL OR json_valid(DeliveryInstructions)),
            FOREIGN KEY (OrderId) REFERENCES Orders (OrderId) ON DELETE CASCADE,
            FOREIGN KEY (AddressId) REFERENCES Addresses (AddressId) ON DELETE RESTRICT,
            FOREIGN KEY (DriverId) REFERENCES Drivers (DriverId) ON DELETE SET NULL
        ) STRICT;

        CREATE TABLE Reviews (
            ReviewId INTEGER PRIMARY KEY,
            OrderId INTEGER NOT NULL UNIQUE,
            CustomerId INTEGER NOT NULL,
            PizzaId INTEGER NOT NULL,
            Rating INTEGER NOT NULL CHECK (Rating BETWEEN 1 AND 5),
            Title TEXT NOT NULL,
            ReviewText TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
            IsVerified INTEGER NOT NULL DEFAULT 1 CHECK (IsVerified IN (0, 1)),
            FOREIGN KEY (OrderId) REFERENCES Orders (OrderId) ON DELETE CASCADE,
            FOREIGN KEY (CustomerId) REFERENCES Customers (CustomerId) ON DELETE CASCADE,
            FOREIGN KEY (PizzaId) REFERENCES Pizzas (PizzaId) ON DELETE CASCADE
        ) STRICT;

        CREATE TABLE OrderStatusAudit (
            OrderStatusAuditId INTEGER PRIMARY KEY,
            OrderId INTEGER NOT NULL,
            OldStatus TEXT NOT NULL,
            NewStatus TEXT NOT NULL,
            ChangedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
            ChangedBy TEXT NOT NULL DEFAULT 'Byte Pizza kitchen',
            FOREIGN KEY (OrderId) REFERENCES Orders (OrderId) ON DELETE CASCADE
        ) STRICT;

        CREATE INDEX IX_Addresses_CustomerId ON Addresses (CustomerId);
        CREATE UNIQUE INDEX UX_Customers_EmailLower ON Customers (lower(Email));
        CREATE INDEX IX_Orders_Customer_OrderedAt ON Orders (CustomerId, OrderedAtUtc DESC);
        CREATE INDEX IX_OrderItems_OrderId ON OrderItems (OrderId);
        CREATE INDEX IX_Deliveries_Driver_DeliveredAt ON Deliveries (DriverId, DeliveredAtUtc);
        CREATE INDEX IX_Reviews_Pizza_Rating ON Reviews (PizzaId, Rating DESC);
        CREATE INDEX IX_Orders_Active
            ON Orders (Status, OrderedAtUtc DESC)
            WHERE Status IN ('Preparing', 'Baking', 'OutForDelivery');

        CREATE TRIGGER AuditOrderStatus
        AFTER UPDATE OF Status ON Orders
        WHEN OLD.Status <> NEW.Status
        BEGIN
            INSERT INTO OrderStatusAudit (OrderId, OldStatus, NewStatus)
            VALUES (NEW.OrderId, OLD.Status, NEW.Status);
        END;

        CREATE TRIGGER TouchOrderUpdatedAt
        AFTER UPDATE ON Orders
        WHEN OLD.UpdatedAtUtc = NEW.UpdatedAtUtc
        BEGIN
            UPDATE Orders
            SET UpdatedAtUtc = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
            WHERE OrderId = NEW.OrderId;
        END;

        INSERT INTO BytePizzaMetadata (MetadataId, SchemaVersion, RestaurantName)
        VALUES (1, 1, 'Byte Pizza');

        INSERT INTO PizzaSizes
            (PizzaSizeId, Name, DiameterInches, PriceAdjustmentPence, CalorieMultiplier, SortOrder)
        VALUES
            (1, 'Personal', 8, -200, 0.65, 1),
            (2, 'Medium', 11, 0, 1.00, 2),
            (3, 'Large', 14, 350, 1.45, 3),
            (4, 'Family', 18, 700, 2.10, 4);

        INSERT INTO Pizzas
            (PizzaId, Name, Description, BasePricePence, BaseCalories, HeatLevel,
             IsVegetarian, IsVegan, DietaryMetadata, Thumbnail)
        VALUES
            (1, 'Margherita', 'Tomato, mozzarella and basil: the hello world of pizza.', 995, 720, 0, 1, 0,
             '{"tags":["vegetarian","classic"],"contains_gluten":true}',
             X'89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C4890000000D49444154789C63F8CFC0F01F00050001FF89993D1D0000000049454E44AE426082'),
            (2, 'Pepperoni', 'Crisp pepperoni, mozzarella and rich tomato sauce.', 1195, 890, 1, 0, 0,
             '{"tags":["meaty","bestseller"],"contains_gluten":true}', NULL),
            (3, 'Hawaiian', 'Ham, pineapple and mozzarella. Deliciously controversial.', 1195, 840, 0, 0, 0,
             '{"tags":["sweet","controversial"],"contains_gluten":true}', NULL),
            (4, 'Meat Feast', 'Pepperoni, ham, chicken and extra mozzarella.', 1395, 1080, 1, 0, 0,
             '{"tags":["high_protein","meaty"],"contains_gluten":true}', NULL),
            (5, 'Veggie Supreme', 'Mushrooms, olives, peppers and red onion.', 1245, 780, 0, 1, 0,
             '{"tags":["vegetarian","five_a_day-ish"],"contains_gluten":true}', NULL),
            (6, 'Four Cheese', 'Mozzarella, cheddar, blue cheese and parmesan.', 1295, 990, 0, 1, 0,
             '{"tags":["vegetarian","cheesy"],"contains_gluten":true,"contains_dairy":true}', NULL),
            (7, 'BBQ Chicken', 'Chicken, red onion and smoky BBQ sauce.', 1345, 920, 1, 0, 0,
             '{"tags":["smoky","high_protein"],"contains_gluten":true}', NULL),
            (8, 'Spicy Inferno', 'Jalapenos, chilli, pepperoni and our hottest sauce.', 1395, 940, 5, 0, 0,
             '{"tags":["spicy","challenge"],"contains_gluten":true}', NULL),
            (9, 'The Pacific Disaster', 'Tomato, mozzarella, pineapple and anchovies. The ocean met the tropics. Neither survived.', 1495, 910, 0, 0, 0,
             '{"tags":["controversial","seafood","staff_warning"],"contains_gluten":true,"contains_fish":true}', NULL);

        INSERT INTO Toppings
            (ToppingId, Name, Category, ExtraPricePence, Calories, IsVegetarian, IsVegan, Allergens)
        VALUES
            (1, 'Mozzarella', 'Cheese', 125, 180, 1, 0, '["milk"]'),
            (2, 'Pepperoni', 'Meat', 150, 210, 0, 0, '[]'),
            (3, 'Ham', 'Meat', 140, 130, 0, 0, '[]'),
            (4, 'Pineapple', 'Fruit', 100, 70, 1, 1, '[]'),
            (5, 'Mushrooms', 'Vegetable', 90, 25, 1, 1, '[]'),
            (6, 'Olives', 'Fruit', 90, 80, 1, 1, '[]'),
            (7, 'Jalapenos', 'Vegetable', 90, 20, 1, 1, '[]'),
            (8, 'Chicken', 'Meat', 160, 170, 0, 0, '[]'),
            (9, 'Red Onion', 'Vegetable', 80, 30, 1, 1, '[]'),
            (10, 'Extra Cheese', 'Cheese', 130, 190, 1, 0, '["milk"]'),
            (11, 'Tomato Sauce', 'Sauce', 60, 45, 1, 1, '[]'),
            (12, 'Basil', 'Herb', 50, 5, 1, 1, '[]'),
            (13, 'Mixed Peppers', 'Vegetable', 90, 35, 1, 1, '[]'),
            (14, 'BBQ Sauce', 'Sauce', 70, 90, 1, 1, '[]'),
            (15, 'Anchovies', 'Seafood', 180, 95, 0, 0, '["fish"]');

        INSERT INTO PizzaToppings (PizzaId, ToppingId, Portion, IsRemovable)
        VALUES
            (1, 11, 1, 0), (1, 1, 1, 1), (1, 12, 1, 1),
            (2, 11, 1, 0), (2, 1, 1, 1), (2, 2, 1.5, 1),
            (3, 11, 1, 0), (3, 1, 1, 1), (3, 3, 1, 1), (3, 4, 1, 1),
            (4, 11, 1, 0), (4, 1, 1, 1), (4, 2, 1, 1), (4, 3, 1, 1),
            (4, 8, 1, 1), (4, 10, 1, 1),
            (5, 11, 1, 0), (5, 1, 1, 1), (5, 5, 1, 1), (5, 6, 1, 1),
            (5, 9, 1, 1), (5, 13, 1, 1),
            (6, 11, 1, 0), (6, 1, 1, 1), (6, 10, 2, 1),
            (7, 14, 1, 0), (7, 1, 1, 1), (7, 8, 1.5, 1), (7, 9, 1, 1),
            (8, 11, 1, 0), (8, 1, 1, 1), (8, 2, 1, 1), (8, 7, 2, 1), (8, 9, 1, 1),
            (9, 11, 1, 0), (9, 1, 1, 1), (9, 4, 1, 1), (9, 15, 1.5, 1);

        WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i + 1 FROM n WHERE i < 30)
        INSERT INTO Customers
            (CustomerId, FirstName, LastName, Email, Phone, LoyaltyPoints, MarketingOptIn, CreatedAtUtc)
        SELECT i,
            CASE i % 10 WHEN 0 THEN 'Ada' WHEN 1 THEN 'Grace' WHEN 2 THEN 'Alan'
                WHEN 3 THEN 'Edsger' WHEN 4 THEN 'Barbara' WHEN 5 THEN 'Donald'
                WHEN 6 THEN 'Linus' WHEN 7 THEN 'Margaret' WHEN 8 THEN 'Dennis' ELSE 'Ken' END,
            CASE i % 8 WHEN 0 THEN 'Lovelace' WHEN 1 THEN 'Hopper' WHEN 2 THEN 'Turing'
                WHEN 3 THEN 'Dijkstra' WHEN 4 THEN 'Liskov' WHEN 5 THEN 'Knuth'
                WHEN 6 THEN 'Torvalds' ELSE 'Hamilton' END,
            'pizza.fan' || i || '@bytepizza.example',
            CASE WHEN i % 6 = 0 THEN NULL ELSE '+44 7700 9' || printf('%05d', i) END,
            (i * 137) % 1200,
            i % 2,
            strftime('%Y-%m-%dT%H:%M:%fZ', 'now', printf('-%d days', 30 + i * 9))
        FROM n;

        INSERT INTO Customers
            (CustomerId, FirstName, LastName, Email, Phone, LoyaltyPoints, MarketingOptIn, CreatedAtUtc)
        VALUES
            (31, 'Rowan', 'Finch', 'rowan.finch@bytepizza.example', '+44 7700 942000', 4242, 1,
             datetime('now', '-420 days'));

        WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i + 1 FROM n WHERE i < 36)
        INSERT INTO Addresses
            (AddressId, CustomerId, Label, Line1, Line2, City, Postcode, DeliveryZone,
             Latitude, Longitude, IsDefault)
        SELECT i,
            CASE WHEN i <= 30 THEN i ELSE i - 30 END,
            CASE WHEN i <= 30 THEN 'Home' ELSE 'Work' END,
            (10 + i) || ' ' || CASE i % 6 WHEN 0 THEN 'Binary Road' WHEN 1 THEN 'Kernel Close'
                WHEN 2 THEN 'Lambda Lane' WHEN 3 THEN 'Cache Street' WHEN 4 THEN 'Pixel Mews'
                ELSE 'Debug Drive' END,
            CASE WHEN i % 7 = 0 THEN 'Flat ' || (i % 9 + 1) ELSE NULL END,
            CASE i % 5 WHEN 0 THEN 'London' WHEN 1 THEN 'Camden' WHEN 2 THEN 'Hackney'
                WHEN 3 THEN 'Greenwich' ELSE 'Brixton' END,
            'BP' || (i % 9 + 1) || ' ' || printf('%dAZ', i % 8 + 1),
            CASE i % 5 WHEN 0 THEN 'Central' WHEN 1 THEN 'North' WHEN 2 THEN 'East'
                WHEN 3 THEN 'South' ELSE 'West' END,
            51.48 + (i % 12) * 0.006,
            -0.18 + (i % 15) * 0.012,
            CASE WHEN i <= 30 THEN 1 ELSE 0 END
        FROM n;

        -- Customer 21 treats the address book like a stamp collection.
        INSERT INTO Addresses
            (AddressId, CustomerId, Label, Line1, City, Postcode, DeliveryZone, IsDefault)
        VALUES
            (37, 21, 'Studio', '404 Canvas Court', 'Hackney', 'BP4 04A', 'East', 0),
            (38, 21, 'Parents', '1 Legacy Lane', 'Camden', 'BP1 01A', 'North', 0),
            (39, 21, 'Hotel', '200 Temporary Terrace', 'London', 'BP2 00A', 'Central', 0),
            (40, 21, 'Secret Lair', '0 Null Island Mews', 'Greenwich', 'BP0 00A', 'South', 0);

        INSERT INTO Drivers (DriverId, FirstName, LastName, Phone, VehicleType, HireDate, IsActive)
        VALUES
            (1, 'Maya', 'Byte', '+44 7700 910001', 'E-bike', date('now', '-900 days'), 1),
            (2, 'Theo', 'Crust', '+44 7700 910002', 'Scooter', date('now', '-700 days'), 1),
            (3, 'Zara', 'Slice', '+44 7700 910003', 'Bicycle', date('now', '-620 days'), 1),
            (4, 'Owen', 'Stack', '+44 7700 910004', 'Car', date('now', '-480 days'), 1),
            (5, 'Nia', 'Cache', '+44 7700 910005', 'E-bike', date('now', '-365 days'), 1),
            (6, 'Felix', 'Loop', '+44 7700 910006', 'Scooter', date('now', '-220 days'), 1),
            (7, 'Ivy', 'Queue', '+44 7700 910007', 'Bicycle', date('now', '-120 days'), 1),
            (8, 'Sam', 'Packet', '+44 7700 910008', 'Car', date('now', '-60 days'), 0);

        INSERT INTO Promotions
            (PromotionId, Code, Description, DiscountType, DiscountValue, MinimumOrderPence,
             ValidFromUtc, ValidUntilUtc, MaxUses, ParentPromotionId, IsActive)
        VALUES
            (1, 'WELCOME10', 'Ten percent off a first Byte Pizza order', 'Percent', 10, 1200,
             datetime('now', '-365 days'), datetime('now', '+365 days'), NULL, NULL, 1),
            (2, 'SLICE5', 'Five pounds off orders over thirty pounds', 'Fixed', 500, 3000,
             datetime('now', '-90 days'), datetime('now', '+90 days'), 500, NULL, 1),
            (3, 'PINEAPPLE', 'A bold discount for a bold topping choice', 'Percent', 15, 1500,
             datetime('now', '-30 days'), datetime('now', '+30 days'), 100, NULL, 1),
            (4, 'VIPBYTE', 'Loyalty bonus stacked on WELCOME10', 'Fixed', 250, 1800,
             datetime('now', '-60 days'), datetime('now', '+60 days'), 50, 1, 1),
            (5, 'LATEBYTE', 'Expired late-night promotion', 'Percent', 20, 2000,
             datetime('now', '-180 days'), datetime('now', '-90 days'), 100, NULL, 0),
            (6, 'FAMILYLOOP', 'Family-size Friday offer', 'Percent', 12, 2500,
             datetime('now', '-14 days'), datetime('now', '+120 days'), NULL, NULL, 1);

        WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i + 1 FROM n WHERE i < 60)
        INSERT INTO Orders
            (OrderId, CustomerId, PromotionId, OrderType, Status, OrderedAtUtc, RequestedForUtc,
             UpdatedAtUtc, PaymentMetadata, Notes)
        SELECT i,
            CASE WHEN i <= 12 THEN 1 WHEN i <= 24 THEN ((i - 13) % 4) + 2
                ELSE 6 + ((i * 7) % 25) END,
            CASE WHEN i % 13 = 0 THEN 4 WHEN i % 7 = 0 THEN 1
                WHEN i % 11 = 0 THEN 2 ELSE NULL END,
            CASE WHEN i % 4 = 0 THEN 'Collection' ELSE 'Delivery' END,
            'Placed',
            strftime('%Y-%m-%dT%H:%M:%fZ', 'now', printf('-%d hours', i * 17)),
            CASE WHEN i % 6 = 0 THEN strftime('%Y-%m-%dT%H:%M:%fZ', 'now', printf('-%d hours', i * 17 - 3)) ELSE NULL END,
            strftime('%Y-%m-%dT%H:%M:%fZ', 'now', printf('-%d hours', i * 17)),
            json_object(
                'source', CASE i % 3 WHEN 0 THEN 'mobile' WHEN 1 THEN 'web' ELSE 'phone' END,
                'payment', CASE i % 4 WHEN 0 THEN 'apple_pay' WHEN 1 THEN 'card'
                    WHEN 2 THEN 'google_pay' ELSE 'cash' END,
                'contactless', json(CASE WHEN i % 2 = 0 THEN 'true' ELSE 'false' END),
                'transaction_id', 'BYTE-' || printf('%05d', i)),
            CASE i % 15 WHEN 0 THEN 'Birthday order - please draw a pizza in the box'
                WHEN 1 THEN 'Ring the bell once' ELSE NULL END
        FROM n;

        WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i + 1 FROM n WHERE i < 108)
        INSERT INTO OrderItems
            (OrderItemId, OrderId, PizzaId, PizzaSizeId, Quantity, UnitPricePence,
             CustomizationPricePence, DiscountPence, SpecialRequest)
        SELECT i,
            ((i - 1) % 60) + 1,
            ((i * 5 - 1) % 8) + 1,
            ((i * 7 - 1) % 4) + 1,
            CASE WHEN i % 17 = 0 THEN 2 ELSE 1 END,
            (SELECT BasePricePence FROM Pizzas WHERE PizzaId = ((i * 5 - 1) % 8) + 1)
                + (SELECT PriceAdjustmentPence FROM PizzaSizes WHERE PizzaSizeId = ((i * 7 - 1) % 4) + 1),
            0,
            CASE WHEN i % 19 = 0 THEN 150 ELSE 0 END,
            CASE i % 21 WHEN 0 THEN 'Well done, please' WHEN 1 THEN 'Cut into squares' ELSE NULL END
        FROM n;

        WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i + 1 FROM n WHERE i < 28)
        INSERT INTO OrderItemToppings (OrderItemId, ToppingId, Action, PriceAdjustmentPence)
        SELECT ((i * 11 - 1) % 108) + 1,
            ((i * 5 - 1) % 14) + 1,
            CASE WHEN i % 4 = 0 THEN 'Remove' ELSE 'Add' END,
            CASE WHEN i % 4 = 0 THEN 0 ELSE
                (SELECT ExtraPricePence FROM Toppings WHERE ToppingId = ((i * 5 - 1) % 14) + 1) END
        FROM n;

        UPDATE OrderItems
        SET CustomizationPricePence = COALESCE((
            SELECT SUM(PriceAdjustmentPence)
            FROM OrderItemToppings
            WHERE OrderItemToppings.OrderItemId = OrderItems.OrderItemId
        ), 0);

        UPDATE Orders
        SET Status = CASE OrderId % 10
            WHEN 0 THEN 'Cancelled'
            WHEN 1 THEN 'Placed'
            WHEN 2 THEN 'Preparing'
            WHEN 3 THEN 'Baking'
            WHEN 4 THEN 'Ready'
            WHEN 5 THEN 'OutForDelivery'
            ELSE 'Delivered' END;

        INSERT INTO Orders
            (OrderId, CustomerId, OrderType, Status, OrderedAtUtc, UpdatedAtUtc,
             PaymentMetadata, TipPence, Notes)
        VALUES
            (61, 31, 'Collection', 'Placed', datetime('now', '-5 days'), datetime('now', '-5 days'),
             '{"source":"mobile","payment":"apple_pay","contactless":true,"transaction_id":"BYTE-LEGEND"}',
             4200, 'Customer said: surprise me. The kitchen took this personally.'),
            (62, 8, 'Collection', 'Placed', datetime('now', '-18 days'), datetime('now', '-18 days'),
             '{"source":"web","payment":"card","contactless":false,"transaction_id":"BYTE-PAC-01"}', 0, NULL),
            (63, 12, 'Collection', 'Placed', datetime('now', '-24 days'), datetime('now', '-24 days'),
             '{"source":"web","payment":"card","contactless":false,"transaction_id":"BYTE-PAC-02"}', 100, NULL),
            (64, 18, 'Collection', 'Placed', datetime('now', '-31 days'), datetime('now', '-31 days'),
             '{"source":"phone","payment":"cash","contactless":false,"transaction_id":"BYTE-PAC-03"}', 0, NULL),
            (65, 27, 'Collection', 'Placed', datetime('now', '-39 days'), datetime('now', '-39 days'),
             '{"source":"mobile","payment":"google_pay","contactless":true,"transaction_id":"BYTE-PAC-04"}', 250, NULL);

        INSERT INTO OrderItems
            (OrderItemId, OrderId, PizzaId, PizzaSizeId, Quantity, UnitPricePence,
             CustomizationPricePence, DiscountPence, SpecialRequest)
        VALUES
            (109, 61, 9, 3, 1, 1845, 100, 0, 'Extra pineapple. Absolutely no substitutions.'),
            (110, 62, 9, 2, 1, 1495, 0, 0, NULL),
            (111, 63, 9, 2, 1, 1495, 0, 0, NULL),
            (112, 64, 9, 2, 1, 1495, 0, 0, NULL),
            (113, 65, 9, 2, 1, 1495, 0, 0, NULL);

        INSERT INTO OrderItemToppings
            (OrderItemId, ToppingId, Action, PriceAdjustmentPence)
        VALUES (109, 4, 'Add', 100);

        UPDATE Orders SET Status = 'Delivered' WHERE OrderId BETWEEN 61 AND 65;

        INSERT INTO Deliveries
            (DeliveryId, OrderId, AddressId, DriverId, Status, EstimatedArrivalUtc,
             DispatchedAtUtc, DeliveredAtUtc, DistanceKm, DeliveryInstructions)
        SELECT o.OrderId, o.OrderId,
            (SELECT MIN(a.AddressId) FROM Addresses a WHERE a.CustomerId = o.CustomerId),
            CASE WHEN o.Status IN ('OutForDelivery', 'Delivered') THEN ((o.OrderId - 1) % 7) + 1 ELSE NULL END,
            CASE o.Status WHEN 'Delivered' THEN 'Delivered' WHEN 'OutForDelivery' THEN 'OutForDelivery'
                WHEN 'Cancelled' THEN 'Cancelled' ELSE 'Pending' END,
            datetime(o.OrderedAtUtc, printf('+%d minutes', 35 + o.OrderId % 25)),
            CASE WHEN o.Status IN ('OutForDelivery', 'Delivered')
                THEN datetime(o.OrderedAtUtc, printf('+%d minutes', 18 + o.OrderId % 14)) ELSE NULL END,
            CASE WHEN o.Status = 'Delivered'
                THEN datetime(o.OrderedAtUtc, printf('+%d minutes', 31 + o.OrderId % 31)) ELSE NULL END,
            round(0.8 + (o.OrderId % 17) * 0.35, 2),
            CASE o.OrderId % 5 WHEN 0 THEN '{"leave_at_door":true,"door_code":"1010"}'
                WHEN 1 THEN '{"leave_at_door":false,"note":"Call on arrival"}' ELSE NULL END
        FROM Orders o
        WHERE o.OrderType = 'Delivery';

        INSERT INTO Reviews
            (ReviewId, OrderId, CustomerId, PizzaId, Rating, Title, ReviewText, CreatedAtUtc)
        SELECT o.OrderId, o.OrderId, o.CustomerId,
            (SELECT PizzaId FROM OrderItems oi WHERE oi.OrderId = o.OrderId ORDER BY oi.OrderItemId LIMIT 1),
            CASE o.OrderId % 8 WHEN 0 THEN 2 WHEN 1 THEN 3 WHEN 2 THEN 4 ELSE 5 END,
            CASE o.OrderId % 6 WHEN 0 THEN 'Perfect slice' WHEN 1 THEN 'Fast and tasty'
                WHEN 2 THEN 'Cheese dreams' WHEN 3 THEN 'Good, but late'
                WHEN 4 THEN 'Family favourite' ELSE 'Would order again' END,
            CASE o.OrderId % 9 WHEN 0 THEN 'Arrived a little cold, but the crust was excellent.'
                WHEN 1 THEN 'The driver was late but friendly and the pizza was hot.'
                WHEN 2 THEN 'Best pepperoni crunch in town.'
                WHEN 3 THEN 'Pineapple absolutely belongs on pizza.'
                WHEN 4 THEN 'Fresh toppings and a wonderfully crisp base.'
                WHEN 5 THEN 'Spicy Inferno lived up to its name!'
                WHEN 6 THEN 'Quick delivery and generous cheese.'
                WHEN 7 THEN 'Great vegetarian options for everyone.'
                ELSE 'A reliable Friday-night Byte Pizza.' END,
            datetime(o.OrderedAtUtc, '+2 hours')
        FROM Orders o
        WHERE o.Status = 'Delivered' AND o.OrderId <= 60;

        INSERT INTO Reviews
            (ReviewId, OrderId, CustomerId, PizzaId, Rating, Title, ReviewText, CreatedAtUtc)
        VALUES
            (61, 61, 31, 9, 5, 'Finally', 'Finally. Someone understands pizza.', datetime('now', '-5 days', '+2 hours')),
            (62, 62, 8, 9, 1, 'I was warned', 'I knew what I was ordering. I still was not prepared.', datetime('now', '-18 days', '+2 hours')),
            (63, 63, 12, 9, 2, 'A culinary crime', 'Pineapple was fine. Anchovies were fine. Together they have committed a crime.', datetime('now', '-24 days', '+2 hours')),
            (64, 64, 18, 9, 2, 'The name is accurate', 'A salty tropical incident that should remain in the test environment.', datetime('now', '-31 days', '+2 hours')),
            (65, 65, 27, 9, 3, 'Confusingly edible', 'Every bite raised more questions, but somehow I finished it.', datetime('now', '-39 days', '+2 hours'));

        -- Curated oddities make exploratory queries reveal small, connected stories.
        UPDATE Customers SET LoyaltyPoints = 9001 WHERE CustomerId = 13;

        UPDATE Orders
        SET TipPence = 1337,
            Notes = 'Production deploy failed at midnight. Send family pizzas and no questions.'
        WHERE OrderId = 8;

        UPDATE Orders SET TipPence = 1 WHERE OrderId = 17;
        UPDATE OrderItems
        SET SpecialRequest = 'Exactly seven slices. Eight would be showing off.'
        WHERE OrderItemId = 77;

        UPDATE Orders
        SET TipPence = 500,
            Notes = 'Cancelled twelve seconds after ordering. The tip somehow survived.'
        WHERE OrderId = 20;

        UPDATE Reviews
        SET Title = 'Wrong order, right outcome',
            ReviewText = 'I ordered Margherita and received pepperoni. Best pepperoni in town, so five stars.'
        WHERE OrderId = 29;

        UPDATE Reviews
        SET Title = 'Wrong branch',
            ReviewText = 'Walked to the other Byte Pizza, came back cold, still ate every slice.'
        WHERE OrderId = 36;

        UPDATE Deliveries
        SET DispatchedAtUtc = datetime((SELECT OrderedAtUtc FROM Orders WHERE OrderId = 37), '+20 minutes'),
            DeliveredAtUtc = datetime((SELECT OrderedAtUtc FROM Orders WHERE OrderId = 37), '+30 minutes')
        WHERE OrderId = 37;
        UPDATE Reviews
        SET Title = 'Too fast?',
            ReviewText = 'The app said 45 minutes. It arrived in ten. I was not dressed for pizza yet.'
        WHERE OrderId = 37;

        UPDATE Deliveries
        SET DispatchedAtUtc = datetime((SELECT OrderedAtUtc FROM Orders WHERE OrderId = 49), '+22 minutes'),
            DeliveredAtUtc = datetime((SELECT OrderedAtUtc FROM Orders WHERE OrderId = 49), '+31 minutes')
        WHERE OrderId = 49;
        UPDATE Reviews
        SET Rating = 5,
            Title = 'Ivy bent spacetime',
            ReviewText = 'Six kilometres by bicycle in nine minutes. Pizza hot. Physics questionable.'
        WHERE OrderId = 49;

        UPDATE Deliveries
        SET DeliveredAtUtc = datetime((SELECT OrderedAtUtc FROM Orders WHERE OrderId = 19), '+145 minutes'),
            DeliveryInstructions = '{"leave_at_door":false,"delay_reason":"driver rescued an escaped parrot"}'
        WHERE OrderId = 19;
        UPDATE Reviews
        SET Title = 'Late, for a good reason',
            ReviewText = 'Two hours late because the driver rescued a parrot. Pizza warm, parrot safe, five stars.'
        WHERE OrderId = 19;

        UPDATE OrderItems
        SET SpecialRequest = 'Seven slices exactly; this is for a database team.'
        WHERE OrderItemId = 106;
        UPDATE Reviews
        SET Rating = 4,
            Title = 'A counting problem',
            ReviewText = 'Asked for seven slices, received eight. Counted twice. Pizza still excellent.'
        WHERE OrderId = 46;

        -- Pineapple is popular on the menu and also the most deliberately removed topping.
        INSERT INTO OrderItemToppings
            (OrderItemId, ToppingId, Action, PriceAdjustmentPence)
        VALUES
            (7, 4, 'Remove', 0),
            (47, 4, 'Remove', 0),
            (79, 4, 'Remove', 0),
            (87, 4, 'Remove', 0);

        -- One customer ordered a Meat Feast and removed every meat topping.
        INSERT INTO OrderItemToppings
            (OrderItemId, ToppingId, Action, PriceAdjustmentPence)
        VALUES
            (28, 2, 'Remove', 0),
            (28, 3, 'Remove', 0),
            (28, 8, 'Remove', 0),
            (28, 10, 'Remove', 0);
        UPDATE Reviews
        SET Title = 'Excellent tomato bread',
            ReviewText = 'Ordered the Meat Feast, removed every meat and the extra cheese. No regrets.'
        WHERE OrderId = 28;

        -- PINEAPPLE works once for a Hawaiian order and once for an order with no pineapple at all.
        UPDATE Orders SET PromotionId = 3 WHERE OrderId IN (38, 47);
        UPDATE Orders
        SET Notes = 'PINEAPPLE code accepted despite there being no pineapple in this order.'
        WHERE OrderId = 38;

        INSERT INTO OrderItemToppings
            (OrderItemId, ToppingId, Action, PriceAdjustmentPence)
        VALUES (57, 4, 'Add', 100);
        UPDATE OrderItems SET CustomizationPricePence = CustomizationPricePence + 100 WHERE OrderItemId = 57;
        UPDATE Reviews
        SET Title = 'A convert',
            ReviewText = 'Added pineapple to the Veggie Supreme. I understand everything now.'
        WHERE OrderId = 57;

        CREATE VIRTUAL TABLE MenuSearch USING fts5(
            EntityType UNINDEXED,
            EntityId UNINDEXED,
            Title,
            Body,
            tokenize = 'unicode61 remove_diacritics 2'
        );

        INSERT INTO MenuSearch (EntityType, EntityId, Title, Body)
        SELECT 'Pizza', PizzaId, Name, Description FROM Pizzas;

        INSERT INTO MenuSearch (EntityType, EntityId, Title, Body)
        SELECT 'Review', ReviewId, Title, ReviewText FROM Reviews;

        CREATE VIEW vw_CurrentMenu AS
        SELECT p.PizzaId,
            p.Name AS PizzaName,
            s.Name AS Size,
            s.DiameterInches,
            round((p.BasePricePence + s.PriceAdjustmentPence) / 100.0, 2) AS Price,
            CAST(round(p.BaseCalories * s.CalorieMultiplier) AS INTEGER) AS EstimatedCalories,
            p.HeatLevel,
            p.IsVegetarian,
            p.IsVegan,
            round((SELECT AVG(r.Rating) FROM Reviews r WHERE r.PizzaId = p.PizzaId), 1) AS AverageRating,
            group_concat(t.Name, ', ') AS Toppings,
            p.Description
        FROM Pizzas p
        CROSS JOIN PizzaSizes s
        JOIN PizzaToppings pt ON pt.PizzaId = p.PizzaId
        JOIN Toppings t ON t.ToppingId = pt.ToppingId
        WHERE p.IsActive = 1
        GROUP BY p.PizzaId, p.Name, s.PizzaSizeId, s.Name, s.DiameterInches,
            p.BasePricePence, s.PriceAdjustmentPence, p.BaseCalories, s.CalorieMultiplier,
            p.HeatLevel, p.IsVegetarian, p.IsVegan, p.Description;

        CREATE VIEW vw_OrderSummary AS
        SELECT o.OrderId,
            c.FirstName || ' ' || c.LastName AS CustomerName,
            o.OrderedAtUtc,
            o.OrderType,
            o.Status,
            COUNT(oi.OrderItemId) AS LineCount,
            SUM(oi.Quantity) AS PizzaCount,
            round(SUM(oi.LineTotalPence) / 100.0, 2) AS Subtotal,
            pr.Code AS PromotionCode,
            round((SUM(oi.LineTotalPence) -
                CASE
                    WHEN pr.PromotionId IS NULL OR SUM(oi.LineTotalPence) < pr.MinimumOrderPence THEN 0
                    WHEN pr.DiscountType = 'Percent' THEN SUM(oi.LineTotalPence) * pr.DiscountValue / 100
                    ELSE MIN(pr.DiscountValue, SUM(oi.LineTotalPence))
                END) / 100.0, 2) AS OrderTotal,
            round(o.TipPence / 100.0, 2) AS Tip,
            round((SUM(oi.LineTotalPence) -
                CASE
                    WHEN pr.PromotionId IS NULL OR SUM(oi.LineTotalPence) < pr.MinimumOrderPence THEN 0
                    WHEN pr.DiscountType = 'Percent' THEN SUM(oi.LineTotalPence) * pr.DiscountValue / 100
                    ELSE MIN(pr.DiscountValue, SUM(oi.LineTotalPence))
                END + o.TipPence) / 100.0, 2) AS GrandTotal
        FROM Orders o
        JOIN Customers c ON c.CustomerId = o.CustomerId
        JOIN OrderItems oi ON oi.OrderId = o.OrderId
        LEFT JOIN Promotions pr ON pr.PromotionId = o.PromotionId
        GROUP BY o.OrderId, c.FirstName, c.LastName, o.OrderedAtUtc, o.OrderType, o.Status,
            pr.PromotionId, pr.Code, pr.MinimumOrderPence, pr.DiscountType, pr.DiscountValue,
            o.TipPence;

        CREATE VIEW vw_PopularPizzas AS
        SELECT p.PizzaId,
            p.Name AS PizzaName,
            SUM(oi.Quantity) AS PizzasSold,
            COUNT(DISTINCT oi.OrderId) AS OrderCount,
            round(SUM(oi.LineTotalPence) / 100.0, 2) AS Revenue,
            round((SELECT AVG(r.Rating) FROM Reviews r WHERE r.PizzaId = p.PizzaId), 2) AS AverageRating
        FROM Pizzas p
        JOIN OrderItems oi ON oi.PizzaId = p.PizzaId
        JOIN Orders o ON o.OrderId = oi.OrderId AND o.Status <> 'Cancelled'
        GROUP BY p.PizzaId, p.Name;
        """;
}
