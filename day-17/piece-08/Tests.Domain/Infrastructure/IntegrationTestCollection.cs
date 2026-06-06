using Xunit;

namespace Tests.Domain.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────────
//  IntegrationTestCollection
//
//  Declares the xUnit collection that all HTTP integration test classes belong
//  to.  A single SqlServerContainerFixture instance is created before the
//  first test class runs and torn down after the last one completes.
//
//  Any test class decorated with [Collection("IntegrationTests")] receives the
//  shared fixture injected into its constructor — no per-class container cold
//  start.  Each class still gets its own isolated SQL Server database via
//  CustomWebApplicationFactory's unique database name.
// ─────────────────────────────────────────────────────────────────────────────
[CollectionDefinition("IntegrationTests")]
public sealed class IntegrationTestCollection : ICollectionFixture<SqlServerContainerFixture>
{
    // Marker class — no implementation needed.
}
