using Microsoft.EntityFrameworkCore;
using RefactorApi.Data;
using RefactorApi.Models;
using RefactorApi.Repositories.Interfaces;

namespace RefactorApi.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _context;

    public OrderRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Customer?> GetCustomerByIdAsync(
        string customerId,
        CancellationToken cancellationToken)
    {
        return await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
    }

    public async Task<Product?> GetProductByIdAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        return await _context.Products
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
    }

    public async Task<Coupon?> GetCouponByCodeAsync(
        string couponCode,
        CancellationToken cancellationToken)
    {
        return await _context.Coupons
            .FirstOrDefaultAsync(c => c.Code == couponCode, cancellationToken);
    }

    public async Task CreateOrderAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        await _context.Orders.AddAsync(order, cancellationToken);

        await _context.OrderItems.AddRangeAsync(order.Items, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}