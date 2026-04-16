using System.Data;
using Microsoft.Data.SqlClient;

// ---------------------------------------------------------------------------
// Host setup
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "WindowsSqlService");
builder.WebHost.UseUrls("http://+:5055");

var connStr = Environment.GetEnvironmentVariable("WPS_SQL_CONNECTION_STRING")
    ?? @"Server=localhost\SQLEXPRESS;Database=WpsDemo;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5;";

var app = builder.Build();

// ---------------------------------------------------------------------------
// Database initialisation — idempotent, runs at startup
// ---------------------------------------------------------------------------
try
{
    await DbSetup.InitializeAsync(connStr);
    app.Logger.LogInformation("WpsDemo database initialised.");
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Database initialisation failed — SQL endpoints may return errors until SQL Server Express is available.");
}

// ---------------------------------------------------------------------------
// GET /api/status
// ---------------------------------------------------------------------------
app.MapGet("/api/status", async () =>
{
    try
    {
        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Products";
        var count = (int)(await cmd.ExecuteScalarAsync())!;
        return Results.Ok(new { status = "ok", database = "WpsDemo", productCount = count });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { status = "error", error = ex.Message });
    }
});

// ---------------------------------------------------------------------------
// GET /api/products
// ---------------------------------------------------------------------------
app.MapGet("/api/products", async () =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT Id, Name, Description, Price, Category, Stock, CreatedAt FROM Products ORDER BY Category, Name";
    using var rdr = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await rdr.ReadAsync())
        rows.Add(new
        {
            id          = rdr.GetInt32(0),
            name        = rdr.GetString(1),
            description = rdr.GetString(2),
            price       = rdr.GetDecimal(3),
            category    = rdr.GetString(4),
            stock       = rdr.GetInt32(5),
            createdAt   = rdr.GetDateTime(6),
        });
    return Results.Ok(rows);
});

// ---------------------------------------------------------------------------
// GET /api/customers/{id}
// ---------------------------------------------------------------------------
app.MapGet("/api/customers/{id:int}", async (int id) =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT Id, FirstName, LastName, Email, CreatedAt FROM Customers WHERE Id = @Id";
    cmd.Parameters.AddWithValue("@Id", id);
    using var rdr = await cmd.ExecuteReaderAsync();
    if (!await rdr.ReadAsync()) return Results.NotFound(new { error = $"Customer {id} not found" });
    return Results.Ok(new
    {
        id        = rdr.GetInt32(0),
        firstName = rdr.GetString(1),
        lastName  = rdr.GetString(2),
        email     = rdr.GetString(3),
        createdAt = rdr.GetDateTime(4),
    });
});

// ---------------------------------------------------------------------------
// GET /api/orders
// ---------------------------------------------------------------------------
app.MapGet("/api/orders", async () =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT o.Id, c.FirstName + ' ' + c.LastName AS Customer,
               p.Name AS Product,
               o.Quantity, o.UnitPrice, o.TotalPrice, o.Status, o.OrderDate
        FROM   Orders o
        JOIN   Customers c ON c.Id = o.CustomerId
        JOIN   Products  p ON p.Id = o.ProductId
        ORDER  BY o.OrderDate DESC";
    using var rdr = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await rdr.ReadAsync())
        rows.Add(new
        {
            id         = rdr.GetInt32(0),
            customer   = rdr.GetString(1),
            product    = rdr.GetString(2),
            quantity   = rdr.GetInt32(3),
            unitPrice  = rdr.GetDecimal(4),
            totalPrice = rdr.GetDecimal(5),
            status     = rdr.GetString(6),
            orderDate  = rdr.GetDateTime(7),
        });
    return Results.Ok(rows);
});

// ---------------------------------------------------------------------------
// POST /api/orders
// Body: { "customerId": 1, "productId": 2, "quantity": 3 }
// ---------------------------------------------------------------------------
app.MapPost("/api/orders", async (OrderRequest req) =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();

    // Fetch unit price from Products
    decimal unitPrice;
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Price FROM Products WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", req.ProductId);
        var val = await cmd.ExecuteScalarAsync();
        if (val is null or DBNull)
            return Results.NotFound(new { error = $"Product {req.ProductId} not found" });
        unitPrice = (decimal)val;
    }

    int newId;
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Orders (CustomerId, ProductId, Quantity, UnitPrice, TotalPrice, Status, OrderDate)
            OUTPUT INSERTED.Id
            VALUES (@CustomerId, @ProductId, @Qty, @UnitPrice, @Total, 'Pending', GETUTCDATE())";
        cmd.Parameters.AddWithValue("@CustomerId", req.CustomerId);
        cmd.Parameters.AddWithValue("@ProductId",  req.ProductId);
        cmd.Parameters.AddWithValue("@Qty",        req.Quantity);
        cmd.Parameters.AddWithValue("@UnitPrice",  unitPrice);
        cmd.Parameters.AddWithValue("@Total",      unitPrice * req.Quantity);
        newId = (int)(await cmd.ExecuteScalarAsync())!;
    }

    return Results.Created($"/api/orders/{newId}", new
    {
        id         = newId,
        status     = "Pending",
        totalPrice = unitPrice * req.Quantity,
    });
});

// ---------------------------------------------------------------------------
// GET /api/reports/summary  — sp_GetOrderSummary
// ---------------------------------------------------------------------------
app.MapGet("/api/reports/summary", async () =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    // CommandType.Text + EXEC so Datadog DBM captures a real query fingerprint
    // rather than an RPC call that only shows the procedure name.
    cmd.CommandText = "EXEC sp_GetOrderSummary";
    using var rdr = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await rdr.ReadAsync())
        rows.Add(new
        {
            status       = rdr.GetString(0),
            orderCount   = rdr.GetInt32(1),
            totalRevenue = rdr.GetDecimal(2),
        });
    return Results.Ok(rows);
});

// ---------------------------------------------------------------------------
// GET /api/customers/{id}/orders  — sp_GetCustomerOrders
// ---------------------------------------------------------------------------
app.MapGet("/api/customers/{id:int}/orders", async (int id) =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "EXEC sp_GetCustomerOrders @CustomerId";
    cmd.Parameters.AddWithValue("@CustomerId", id);
    using var rdr = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await rdr.ReadAsync())
        rows.Add(new
        {
            orderId    = rdr.GetInt32(0),
            product    = rdr.GetString(1),
            quantity   = rdr.GetInt32(2),
            totalPrice = rdr.GetDecimal(3),
            status     = rdr.GetString(4),
            orderDate  = rdr.GetDateTime(5),
        });
    if (rows.Count == 0) return Results.NotFound(new { error = $"No orders found for customer {id}" });
    return Results.Ok(rows);
});

await app.RunAsync();

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------
record OrderRequest(int CustomerId, int ProductId, int Quantity);

// ---------------------------------------------------------------------------
// Database setup — runs once at startup; all DDL is idempotent
// ---------------------------------------------------------------------------
static class DbSetup
{
    public static async Task InitializeAsync(string connStr)
    {
        // 1. Create the WpsDemo database if it doesn't exist (connect to master)
        var masterStr = new SqlConnectionStringBuilder(connStr)
            { InitialCatalog = "master" }.ToString();
        using (var conn = new SqlConnection(masterStr))
        {
            await conn.OpenAsync();
            await ExecAsync(conn, @"
                IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = 'WpsDemo')
                    CREATE DATABASE WpsDemo;");
        }

        // 2. Tables, stored procs, and seed data (connect to WpsDemo)
        using (var conn = new SqlConnection(connStr))
        {
            await conn.OpenAsync();

            await ExecAsync(conn, @"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Products')
                CREATE TABLE Products (
                    Id          INT IDENTITY(1,1) PRIMARY KEY,
                    Name        NVARCHAR(100) NOT NULL,
                    Description NVARCHAR(500) NOT NULL,
                    Price       DECIMAL(10,2) NOT NULL,
                    Category    NVARCHAR(50)  NOT NULL,
                    Stock       INT           NOT NULL DEFAULT 100,
                    CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE()
                );");

            await ExecAsync(conn, @"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Customers')
                CREATE TABLE Customers (
                    Id          INT IDENTITY(1,1) PRIMARY KEY,
                    FirstName   NVARCHAR(50)  NOT NULL,
                    LastName    NVARCHAR(50)  NOT NULL,
                    Email       NVARCHAR(200) NOT NULL UNIQUE,
                    CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE()
                );");

            await ExecAsync(conn, @"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Orders')
                CREATE TABLE Orders (
                    Id          INT IDENTITY(1,1) PRIMARY KEY,
                    CustomerId  INT           NOT NULL REFERENCES Customers(Id),
                    ProductId   INT           NOT NULL REFERENCES Products(Id),
                    Quantity    INT           NOT NULL DEFAULT 1,
                    UnitPrice   DECIMAL(10,2) NOT NULL,
                    TotalPrice  DECIMAL(10,2) NOT NULL,
                    Status      NVARCHAR(20)  NOT NULL DEFAULT 'Pending',
                    OrderDate   DATETIME2     NOT NULL DEFAULT GETUTCDATE()
                );");

            await ExecAsync(conn, @"
                CREATE OR ALTER PROCEDURE sp_GetOrderSummary AS
                SELECT   Status,
                         COUNT(1)        AS OrderCount,
                         SUM(TotalPrice) AS TotalRevenue
                FROM     Orders
                GROUP BY Status
                ORDER BY Status;");

            await ExecAsync(conn, @"
                CREATE OR ALTER PROCEDURE sp_GetCustomerOrders
                    @CustomerId INT
                AS
                SELECT o.Id, p.Name AS Product, o.Quantity, o.TotalPrice, o.Status, o.OrderDate
                FROM   Orders   o
                JOIN   Products p ON p.Id = o.ProductId
                WHERE  o.CustomerId = @CustomerId
                ORDER  BY o.OrderDate DESC;");

            // Seed — only if tables are empty
            await ExecAsync(conn, @"
                IF NOT EXISTS (SELECT 1 FROM Products)
                INSERT INTO Products (Name, Description, Price, Category, Stock) VALUES
                ('Datadog APM',        'Application performance monitoring — distributed traces and flame graphs',  299.00, 'Observability',  50),
                ('Datadog Logs',       'Log management with live tail, search, and ML-based anomaly detection',    199.00, 'Observability',  75),
                ('Datadog RUM',        'Real user monitoring for web and mobile apps',                             149.00, 'Observability',  80),
                ('Datadog DBM',        'Database monitoring with query-level metrics and explain plans',           249.00, 'Observability',  60),
                ('Datadog Synthetics', 'Automated API and browser tests from 30+ global locations',               179.00, 'Testing',        90),
                ('Datadog Dashboards', 'Customisable dashboards with 600+ integrations and live data',             99.00, 'Visualisation', 120);");

            await ExecAsync(conn, @"
                IF NOT EXISTS (SELECT 1 FROM Customers)
                INSERT INTO Customers (FirstName, LastName, Email) VALUES
                ('Alice', 'Observability', 'alice@example.com'),
                ('Bob',   'Monitoring',    'bob@example.com'),
                ('Carol', 'DevOps',        'carol@example.com'),
                ('Dave',  'Platform',      'dave@example.com');");

            await ExecAsync(conn, @"
                IF NOT EXISTS (SELECT 1 FROM Orders)
                BEGIN
                    DECLARE @apm   INT = (SELECT Id FROM Products WHERE Name = 'Datadog APM');
                    DECLARE @logs  INT = (SELECT Id FROM Products WHERE Name = 'Datadog Logs');
                    DECLARE @rum   INT = (SELECT Id FROM Products WHERE Name = 'Datadog RUM');
                    DECLARE @dbm   INT = (SELECT Id FROM Products WHERE Name = 'Datadog DBM');
                    DECLARE @syn   INT = (SELECT Id FROM Products WHERE Name = 'Datadog Synthetics');
                    DECLARE @dash  INT = (SELECT Id FROM Products WHERE Name = 'Datadog Dashboards');
                    DECLARE @alice INT = (SELECT Id FROM Customers WHERE Email = 'alice@example.com');
                    DECLARE @bob   INT = (SELECT Id FROM Customers WHERE Email = 'bob@example.com');
                    DECLARE @carol INT = (SELECT Id FROM Customers WHERE Email = 'carol@example.com');
                    DECLARE @dave  INT = (SELECT Id FROM Customers WHERE Email = 'dave@example.com');

                    INSERT INTO Orders (CustomerId, ProductId, Quantity, UnitPrice, TotalPrice, Status, OrderDate) VALUES
                    (@alice, @apm,  1, 299.00,  299.00, 'Delivered',  DATEADD(day, -30, GETUTCDATE())),
                    (@alice, @logs, 2, 199.00,  398.00, 'Delivered',  DATEADD(day, -25, GETUTCDATE())),
                    (@bob,   @rum,  1, 149.00,  149.00, 'Delivered',  DATEADD(day, -20, GETUTCDATE())),
                    (@bob,   @dbm,  1, 249.00,  249.00, 'Processing', DATEADD(day, -10, GETUTCDATE())),
                    (@carol, @syn,  3, 179.00,  537.00, 'Delivered',  DATEADD(day, -15, GETUTCDATE())),
                    (@carol, @apm,  1, 299.00,  299.00, 'Shipped',    DATEADD(day,  -5, GETUTCDATE())),
                    (@dave,  @dash, 5,  99.00,  495.00, 'Delivered',  DATEADD(day, -12, GETUTCDATE())),
                    (@dave,  @logs, 1, 199.00,  199.00, 'Processing', DATEADD(day,  -3, GETUTCDATE())),
                    (@alice, @rum,  2, 149.00,  298.00, 'Pending',    DATEADD(day,  -1, GETUTCDATE())),
                    (@bob,   @syn,  1, 179.00,  179.00, 'Pending',    GETUTCDATE());
                END");
        }
    }

    private static async Task ExecAsync(SqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
