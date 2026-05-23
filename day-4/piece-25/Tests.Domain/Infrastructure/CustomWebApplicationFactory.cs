using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Abstractions;
using QuotesApi.Data;

namespace Tests.Domain.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────────
//  CustomWebApplicationFactory
//
//  Boots the REAL ASP.NET Core pipeline in-memory for integration tests,
//  pointed at a dedicated SQL Server database inside a Testcontainer.
//
//  What is overridden (test infrastructure only):
//    • AppDbContext  → isolated, uniquely-named SQL Server database per instance
//    • IClock        → FakeClock so tests can control time
//
//  What stays REAL (not mocked):
//    • Routing, middleware, auth pipeline (JWT validation, policies, handlers)
//    • EF Core queries, migrations, admin user seeding
//    • Dependency injection container
//    • Request/response serialisation
//
//  Isolation guarantee:
//    Each factory instance gets a database named IntTestDb_{guid}.
//    Program.cs calls Database.Migrate() on startup, which creates that
//    database on the shared container and applies all migrations.
//    DisposeAsync drops the database so no state leaks between test classes.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Unique database name so parallel test classes never collide.
    private readonly string _databaseName = $"IntTestDb_{Guid.NewGuid():N}";

    // Master-level connection string (points at the container's master DB).
    private readonly string _containerConnectionString;

    /// <summary>Exposes the fake clock so individual tests can set/advance time.</summary>
    public FakeClock Clock { get; } = new();

    public CustomWebApplicationFactory(string containerConnectionString)
    {
        _containerConnectionString = containerConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // ── Replace production DbContext options ───────────────────────────
            // Remove the SQL Server options registered by ServiceExtensions and
            // substitute a connection string pointing to the unique test database.
            // Program.cs will call Database.Migrate() which creates this database
            // and applies all migrations before any test runs.
            var dbOpts = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbOpts is not null)
                services.Remove(dbOpts);

            var testConnectionString = new SqlConnectionStringBuilder(_containerConnectionString)
            {
                InitialCatalog = _databaseName,
                TrustServerCertificate = true
            }.ToString();

            services.AddDbContext<AppDbContext>(opts =>
                opts.UseSqlServer(testConnectionString));

            // ── Replace IClock with FakeClock ─────────────────────────────────
            var clockDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IClock));
            if (clockDescriptor is not null)
                services.Remove(clockDescriptor);

            services.AddSingleton<IClock>(Clock);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        // Drop the test-specific database so containers don't accumulate stale DBs.
        // Connect to master and force-close any remaining connections before dropping.
        try
        {
            var masterConnectionString = new SqlConnectionStringBuilder(_containerConnectionString)
            {
                InitialCatalog = "master",
                TrustServerCertificate = true
            }.ToString();

            await using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                IF DB_ID(N'{_databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_databaseName}];
                END
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best-effort cleanup — don't let teardown failures mask test failures.
        }

        await base.DisposeAsync();
    }
}
