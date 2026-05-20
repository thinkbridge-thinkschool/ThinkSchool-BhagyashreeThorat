using RefactorApi.DTOs;
using RefactorApi.Models;
using RefactorApi.Repositories.Interfaces;
using RefactorApi.Services.Interfaces;

namespace RefactorApi.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _repository;
    private readonly IEnumerable<IDiscountStrategy> _discountStrategies;

    public OrderService(
        IOrderRepository repository,
        IEnumerable<IDiscountStrategy> discountStrategies)
    {
        _repository = repository;
        _discountStrategies = discountStrategies;
    }

    public async Task<CreateOrderResponseDto> CreateOrderAsync(
        CreateOrderRequestDto request,
        CancellationToken cancellationToken)
    {
        var customer = await _repository
            .GetCustomerByIdAsync(request.CustomerId, cancellationToken);

        if (customer == null)
        {
            throw new Exception("Customer not found");
        }

        if (!customer.IsActive)
        {
            throw new Exception("Customer is inactive");
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            throw new Exception("No items provided");
        }

        decimal subtotal = 0;

        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            CustomerId = customer.Id,
            CreatedAt = DateTime.UtcNow,
            Status = "Pending"
        };

        foreach (var item in request.Items)
        {
            var product = await _repository
                .GetProductByIdAsync(item.ProductId, cancellationToken);

            if (product == null)
            {
                throw new Exception($"Product not found: {item.ProductId}");
            }

            if (item.Quantity <= 0)
            {
                throw new Exception("Invalid quantity");
            }

            if (product.Stock < item.Quantity)
            {
                throw new Exception($"Insufficient stock for {product.Name}");
            }

            decimal lineTotal = product.Price * item.Quantity;

            subtotal += lineTotal;
            product.Stock -= item.Quantity;

            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = order.Id,
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = item.Quantity,
                UnitPrice = product.Price,
                LineTotal = lineTotal
            });
        }

        decimal shipping = subtotal > 100 ? 0 : 10;
        decimal tax = subtotal * 0.08m;
        decimal total = subtotal + shipping + tax;

        // Run each registered discount strategy in sequence.
        // Adding a new discount rule = register a new IDiscountStrategy; no changes here.
        foreach (var strategy in _discountStrategies)
        {
            total = await strategy.ApplyAsync(total, request, cancellationToken);
        }

        order.Subtotal = subtotal;
        order.Shipping = shipping;
        order.Tax = tax;
        order.Total = total;

        customer.TotalSpend += total;
        customer.LastOrderDate = DateTime.UtcNow;

        await _repository.CreateOrderAsync(order, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateOrderResponseDto
        {
            OrderId = order.Id,
            Total = order.Total,
            Status = order.Status,
            Message = "Order created successfully"
        };
    }
}