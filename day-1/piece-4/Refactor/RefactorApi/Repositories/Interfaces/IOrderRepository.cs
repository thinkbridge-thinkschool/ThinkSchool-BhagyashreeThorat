using RefactorApi.Models;

namespace RefactorApi.Repositories.Interfaces;

public interface IOrderRepository
{
    Task<Customer?> GetCustomerByIdAsync(string customerId, CancellationToken cancellationToken);

    Task<Product?> GetProductByIdAsync(string productId, CancellationToken cancellationToken);

    Task<Coupon?> GetCouponByCodeAsync(string couponCode, CancellationToken cancellationToken);

    Task CreateOrderAsync(Order order, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}