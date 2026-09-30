using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

return await Proof.RunAsync(args);

static class Proof
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--offline"))
                throw new InvalidOperationException("Usage: dotnet run [-- --offline]");

            Console.WriteLine("=== EF Core 11 JsonPathExists proof ===");
            Console.WriteLine($"Runtime: {Environment.Version}");
            Console.WriteLine($"EF Core: {typeof(DbContext).Assembly.GetName().Version}");

            if (args.Contains("--offline"))
            {
                using var offline = new ProofContext(new DbContextOptionsBuilder<ProofContext>()
                    .UseSqlServer("Server=localhost;Database=tempdb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True",
                        sql => sql.UseCompatibilityLevel(160)).Options);
                VerifyTranslation(offline);
                Console.WriteLine("PASS: offline SQL translation checks completed");
                Console.WriteLine("OFFLINE ONLY: no SQL Server connection or result verification was performed.");
                return 0;
            }

            var supplied = Environment.GetEnvironmentVariable("DNC_SQL_CONNECTION");
            if (string.IsNullOrWhiteSpace(supplied))
                throw new InvalidOperationException("Run Verify-JsonPathExists.ps1 -Server 'YOUR_LOCAL_SERVER'.");

            // Always use tempdb. Never create, clear, or drop an application database.
            var settings = new SqlConnectionStringBuilder(supplied)
            {
                InitialCatalog = "tempdb",
                Pooling = false,
                ConnectTimeout = 15,
                ApplicationName = "DNC EF11 JsonPathExists Proof"
            };
            await using var connection = new SqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            await PreflightAsync(connection);

            await using var context = new ProofContext(new DbContextOptionsBuilder<ProofContext>()
                .UseSqlServer(connection, sql => sql.UseCompatibilityLevel(160))
                .Options);
            VerifyTranslation(context);

            await using var transaction = await connection.BeginTransactionAsync();
            await context.Database.UseTransactionAsync(transaction);
            try
            {
                await context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE #DncJsonPathProof (
                        Id int NOT NULL PRIMARY KEY,
                        Name nvarchar(40) NOT NULL,
                        JsonData nvarchar(max) NULL
                    );
                    """);
                var fixtures = Fixtures();
                foreach (var row in fixtures)
                {
                    await context.Database.ExecuteSqlAsync(
                        $"INSERT INTO #DncJsonPathProof (Id, Name, JsonData) VALUES ({row.Id}, {row.Name}, {row.JsonData})");
                }

                Console.WriteLine("\n--- Raw SQL: path existence versus extracted value ---");
                await ShowRawResultsAsync(connection, (SqlTransaction)transaction);

                Console.WriteLine("\n--- Executed EF queries ---");
                await ExpectIdsAsync("Existing OptionalInt path (including JSON null, zero and string)",
                    Existing(context), [2, 3, 4, 5]);
                await ExpectIdsAsync("Missing OptionalInt path in non-null JSON documents",
                    context.Rows.Where(x => x.JsonData != null && !EF.Functions.JsonPathExists(x.JsonData, "$.OptionalInt")),
                    [1, 6, 8]);
                await ExpectIdsAsync("Nested Flag path exists even when its value is false",
                    context.Rows.Where(x => x.JsonData != null && EF.Functions.JsonPathExists(x.JsonData, "$.Nested.Flag")),
                    [6]);
                await ExpectIdsAsync("Nested object path includes an explicit JSON null",
                    context.Rows.Where(x => x.JsonData != null && EF.Functions.JsonPathExists(x.JsonData, "$.Nested")),
                    [6, 8]);
                await ExpectIdsAsync("JSON path key matching is case-sensitive",
                    context.Rows.Where(x => x.JsonData != null && EF.Functions.JsonPathExists(x.JsonData, "$.optionalint")),
                    []);
                await ExpectIdsAsync("SQL NULL document remains separate from a JSON null property",
                    context.Rows.Where(x => x.JsonData == null), [7]);
                Console.WriteLine("PASS: missing property and explicit JSON null are distinguished");
            }
            finally
            {
                await transaction.RollbackAsync();
                Console.WriteLine("PASS: temporary table and test rows rolled back");
            }

            Console.WriteLine("PASS: EF11 JsonPathExists proof completed");
            return 0;
        }
        catch (SqlException ex)
        {
            // Do not echo the connection string or credentials into screenshot/log evidence.
            Console.Error.WriteLine($"FAIL: SQL Server error {ex.Number}.");
            Console.Error.WriteLine("Check the server name, service, local login permissions, and SQL Server 2022+ version. See README-AR.md.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex.Message}");
            return 1;
        }
    }

    static IQueryable<ProofRow> Existing(ProofContext context) => context.Rows
        .Where(x => x.JsonData != null && EF.Functions.JsonPathExists(x.JsonData, "$.OptionalInt"));

    static void VerifyTranslation(ProofContext context)
    {
        Console.WriteLine("\n--- Generated SQL for the main EF query ---");
        var sql = Existing(context).OrderBy(x => x.Id).ToQueryString();
        Console.WriteLine(sql);
        Require(sql.Contains("JSON_PATH_EXISTS", StringComparison.Ordinal), "EF query did not translate to JSON_PATH_EXISTS.");
        Require(sql.Contains("$.OptionalInt", StringComparison.Ordinal), "Generated SQL lost the JSON path.");
        var nested = context.Rows.Where(x => x.JsonData != null
            && EF.Functions.JsonPathExists(x.JsonData, "$.Nested.Flag")).ToQueryString();
        Require(nested.Contains("JSON_PATH_EXISTS", StringComparison.Ordinal)
            && nested.Contains("$.Nested.Flag", StringComparison.Ordinal), "Nested path translation failed.");
        var missing = context.Rows.Where(x => x.JsonData != null
            && !EF.Functions.JsonPathExists(x.JsonData, "$.OptionalInt")).ToQueryString();
        Require(missing.Contains("JSON_PATH_EXISTS", StringComparison.Ordinal), "Negated path translation failed.");
        Console.WriteLine("PASS: main, nested and negated LINQ queries translate to JSON_PATH_EXISTS");
    }

    static async Task PreflightAsync(SqlConnection connection)
    {
        await using var version = connection.CreateCommand();
        version.CommandText = "SELECT CONVERT(int, SERVERPROPERTY('ProductMajorVersion')), CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion'));";
        await using (var reader = await version.ExecuteReaderAsync())
        {
            Require(await reader.ReadAsync(), "SQL Server version was not returned.");
            var major = reader.GetInt32(0);
            Console.WriteLine($"SQL Server version: {reader.GetString(1)}");
            Require(major >= 16, "This local proof requires SQL Server 2022 (major 16) or newer. An older installation cannot run JSON_PATH_EXISTS.");
        }
        await using var capability = connection.CreateCommand();
        capability.CommandText = "SELECT JSON_PATH_EXISTS(N'{\"OptionalInt\":null}', '$.OptionalInt');";
        Require(Convert.ToInt32(await capability.ExecuteScalarAsync()) == 1, "JSON_PATH_EXISTS capability probe failed.");
        Console.WriteLine("PASS: SQL Server 2022+ and JSON_PATH_EXISTS capability confirmed");
    }

    static async Task ShowRawResultsAsync(SqlConnection connection, SqlTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, Name, JSON_PATH_EXISTS(JsonData, '$.OptionalInt') AS PathExists,
                   JSON_VALUE(JsonData, '$.OptionalInt') AS ExtractedValue
            FROM #DncJsonPathProof ORDER BY Id;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Console.WriteLine("Id | Case           | PathExists | JSON_VALUE");
        var count = 0;
        while (await reader.ReadAsync())
        {
            count++;
            var id = reader.GetInt32(0);
            int? exists = reader.IsDBNull(2) ? null : reader.GetInt32(2);
            var value = reader.IsDBNull(3) ? "NULL" : reader.GetString(3);
            Console.WriteLine($"{id,2} | {reader.GetString(1),-14} | {(exists?.ToString() ?? "SQL NULL"),-10} | {value}");
            if (id == 1) Require(exists == 0 && value == "NULL", "Missing-property raw result differs from the expected result.");
            if (id == 2) Require(exists == 1 && value == "NULL", "Explicit JSON null was not identified as an existing path.");
            if (id == 7) Require(exists == null, "SQL NULL must produce SQL NULL, not an existence value.");
        }
        Require(count == 8, "Fixture row count is incorrect.");
        Console.WriteLine("PASS: raw SQL distinguishes missing, JSON null and SQL NULL");
    }

    static async Task ExpectIdsAsync(string label, IQueryable<ProofRow> query, int[] expected)
    {
        var actual = await query.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        Require(actual.SequenceEqual(expected), $"{label}: expected [{string.Join(',', expected)}], got [{string.Join(',', actual)}].");
        Console.WriteLine($"PASS: {label} => [{string.Join(',', actual)}]");
    }

    static ProofRow[] Fixtures() =>
    [
        new(1, "Missing", "{}"),
        new(2, "JSON null", """{"OptionalInt":null}"""),
        new(3, "Zero", """{"OptionalInt":0}"""),
        new(4, "Number", """{"OptionalInt":7}"""),
        new(5, "String", """{"OptionalInt":"text"}"""),
        new(6, "Nested false", """{"Nested":{"Flag":false}}"""),
        new(7, "SQL NULL", null),
        new(8, "Nested null", """{"Nested":null}""")
    ];

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

sealed class ProofContext(DbContextOptions<ProofContext> options) : DbContext(options)
{
    public DbSet<ProofRow> Rows => Set<ProofRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProofRow>(row =>
        {
            row.ToTable("#DncJsonPathProof");
            row.HasKey(x => x.Id);
            row.Property(x => x.Id).ValueGeneratedNever();
            row.Property(x => x.Name).HasMaxLength(40);
            row.Property(x => x.JsonData).HasColumnType("nvarchar(max)");
        });
    }
}

sealed class ProofRow(int id, string name, string? jsonData)
{
    public int Id { get; set; } = id;
    public string Name { get; set; } = name;
    public string? JsonData { get; set; } = jsonData;
}
