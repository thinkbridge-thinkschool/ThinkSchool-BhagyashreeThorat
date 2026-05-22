using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Abstractions;
using QuotesApi.Data;

namespace Tests.Domain.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────────
//  CustomWebApplicationFactory
//
//  Boots the REAL ASP.NET Core pipeline in-memory for integration tests.
//
//  What is overridden (test infrastructure only):
//    • AppDbContext  → isolated, named SQLite in-memory DB per factory instance
//    • IClock        → FakeClock so tests can control time
//
//  What stays REAL (not mocked):
//    • Routing, middleware, auth pipeline (JWT validation, policies, handlers)
//    • EF Core queries, migrations, admin user seeding
//    • Dependency injection container
//    • Request/response serialisation
//
//  Isolation guarantee:
//    xUnit creates a new test-class instance per test method.
//    If each test class takes this factory in its constructor (rather than using
//    IClassFixture), every test method gets a completely fresh DB and clock.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Each factory instance owns one named in-memory SQLite database.
    // The Guid suffix guarantees no two factory instances ever share a DB,
    // even when tests run in parallel.
    private readonly SqliteConnection _connection;

    /// <summary>Exposes the fake clock so individual tests can set/advance time.</summary>
    public FakeClock Clock { get; } = new();

    public CustomWebApplicationFactory()
    {
        var dbName = $"IntTestDb_{Guid.NewGuid():N}";
        _connection = new SqliteConnection($"DataSource={dbName};Mode=Memory;Cache=Shared");
        // Keep the connection open for the lifetime of the factory so the
        // in-memory SQLite database is not dropped between DbContext instances.
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // ── Replace production DbContext options ───────────────────────────
            // Remove the Sqlite file-based options registered by ServiceExtensions
            // and substitute an isolated in-memory connection for this test run.
            var dbOpts = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbOpts is not null)
                services.Remove(dbOpts);

            services.AddDbContext<AppDbContext>(opts => opts.UseSqlite(_connection));

            // ── Replace IClock with FakeClock ─────────────────────────────────
            // Removing SystemClock and injecting the shared FakeClock instance
            // lets tests manipulate UtcNow without touching production code.
            var clockDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IClock));
            if (clockDescriptor is not null)
                services.Remove(clockDescriptor);

            services.AddSingleton<IClock>(Clock);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
