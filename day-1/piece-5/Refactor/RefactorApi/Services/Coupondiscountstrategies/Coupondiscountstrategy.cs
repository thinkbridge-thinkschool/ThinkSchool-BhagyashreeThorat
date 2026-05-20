using RefactorApi.DTOs;
using RefactorApi.Repositories.Interfaces;
using RefactorApi.Services.Interfaces;

namespace RefactorApi.Services.DiscountStrategies;

/// <summary>
/// Applies a percentage discount when the request carries a valid, unexpired,
/// and still-redeemable coupon code. Mutates coupon.UsageCount so the caller's
/// SaveChanges persists the increment — matching the original behavior exactly.
/// </summary>
public sealed class CouponDiscountStrategy : IDiscountStrategy
{
    private readonly IOrderRepository _repository;

    public CouponDiscountStrategy(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<decimal> ApplyAsync(
        decimal currentTotal,
        CreateOrderRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CouponCode))
        {
            return currentTotal;
        }

        var coupon = await _repository
            .GetCouponByCodeAsync(request.CouponCode, cancellationToken);

        if (coupon == null
            || coupon.ExpiresAt <= DateTime.UtcNow
            || coupon.UsageCount >= coupon.MaxUsage)
        {
            return currentTotal;
        }

        var discount = currentTotal * (coupon.DiscountPercent / 100m);
        coupon.UsageCount++;

        return currentTotal - discount;
    }
}