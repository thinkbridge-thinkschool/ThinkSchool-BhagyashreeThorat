using Testcontainers.MsSql;
using Xunit;

namespace Tests.Domain.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────────
//  SqlServerContainerFixture
//
//  Manages a single SQL Server 2022 Testcontainer shared across the entire
//  "IntegrationTests" xUnit collection.
//
//  Lifecycle:
//    • InitializeAsync  — pulls image (first run) and starts the container
//    • ConnectionString — master connection string; each factory instance
//                         appends its own unique database name on top
//    • DisposeAsync     — stops and removes the container after all tests finish
//
//  Container is shared (one startup per test run) for speed.
//  DB-level isolation is achieved by each CustomWebApplicationFactory
//  creating and dropping its own uniquely-named database.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    /// <summary>
    /// Master-level connection string for the container.
    /// Each factory instance derives its own DB-specific connection string from this.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
