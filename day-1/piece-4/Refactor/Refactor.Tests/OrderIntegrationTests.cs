using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using RefactorApi.DTOs;
using Xunit;

namespace Refactor.Tests;

public class OrderIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public OrderIntegrationTests(
        WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostOrder_ReturnsServerError_WhenCustomerMissing()
    {
        var request = new CreateOrderRequestDto
        {
            CustomerId = "missing",
            Items = new()
            {
                new OrderItemRequestDto
                {
                    ProductId = "1",
                    Quantity = 1
                }
            }
        };

        var response = await _client
            .PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.InternalServerError,
            response.StatusCode);
    }
}