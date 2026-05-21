using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using Xunit;

namespace QuotesApi.Tests;

public class CancellationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CancellationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace the production SQLite file with an isolated test database
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor is not null)
                    services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite("Data Source=test_cancellation.db"));
            });
        });
    }

    [Fact]
    public async Task GetCollection_WhenTokenCancelledBeforeCompletion_ThrowsOperationCanceledException()
    {
        // Arrange: cancel after 500 ms — well before the 5 000 ms artificial delay
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var client = _factory.CreateClient();

        // Act
        var act = async () => await client.GetAsync("/api/collections/1", cts.Token);

        // Assert: HttpClient propagates the cancelled token as OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);

        // Confirm the token really was cancelled (not a different failure)
        Assert.True(cts.IsCancellationRequested, "CancellationToken should have been cancelled");
    }
}
