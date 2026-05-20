using Moq;
using RefactorApi.DTOs;
using RefactorApi.Models;
using RefactorApi.Repositories.Interfaces;
using RefactorApi.Services;
using Xunit;

namespace Refactor.Tests;

public class OrderServiceTests
{
    private readonly Mock<IOrderRepository> _repositoryMock;

    private readonly OrderService _service;

    public OrderServiceTests()
    {
        _repositoryMock = new Mock<IOrderRepository>();

        _service = new OrderService(_repositoryMock.Object);
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldThrow_WhenCustomerNotFound()
    {
        var request = new CreateOrderRequestDto
        {
            CustomerId = "invalid",
            Items = new()
            {
                new OrderItemRequestDto
                {
                    ProductId = "1",
                    Quantity = 1
                }
            }
        };

        _repositoryMock
            .Setup(r => r.GetCustomerByIdAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        await Assert.ThrowsAsync<Exception>(() =>
            _service.CreateOrderAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldThrow_WhenQuantityInvalid()
    {
        var customer = new Customer
        {
            Id = "1",
            IsActive = true
        };

        var product = new Product
        {
            Id = "1",
            Name = "Laptop",
            Price = 1000,
            Stock = 10
        };

        var request = new CreateOrderRequestDto
        {
            CustomerId = "1",
            Items = new()
            {
                new OrderItemRequestDto
                {
                    ProductId = "1",
                    Quantity = 0
                }
            }
        };

        _repositoryMock
            .Setup(r => r.GetCustomerByIdAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        _repositoryMock
            .Setup(r => r.GetProductByIdAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        await Assert.ThrowsAsync<Exception>(() =>
            _service.CreateOrderAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldCreateOrder_WhenRequestValid()
    {
        var customer = new Customer
        {
            Id = "1",
            IsActive = true
        };

        var product = new Product
        {
            Id = "1",
            Name = "Laptop",
            Price = 1000,
            Stock = 10
        };

        var request = new CreateOrderRequestDto
        {
            CustomerId = "1",
            Items = new()
            {
                new OrderItemRequestDto
                {
                    ProductId = "1",
                    Quantity = 1
                }
            }
        };

        _repositoryMock
            .Setup(r => r.GetCustomerByIdAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        _repositoryMock
            .Setup(r => r.GetProductByIdAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var result = await _service
            .CreateOrderAsync(request, CancellationToken.None);

        Assert.NotNull(result);

        Assert.Equal("Pending", result.Status);
    }
}

//Test: validation rejects orders with negative quantity, zero quantity, and missing items. Test: valid order creates successfully with expected status.
//Test: coupon discount is applied correctly when valid code is provided, and total is unchanged when invalid code is used.
//Test: inactive customers cannot place orders, and appropriate exceptions are thrown for all validation failures.