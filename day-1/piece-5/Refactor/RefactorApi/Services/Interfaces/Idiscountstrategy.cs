using RefactorApi.DTOs;

namespace RefactorApi.Services.Interfaces;

/// <summary>
/// Applies a discount rule to a running order total.
/// Implementations are registered in DI and executed in order by OrderService.
/// Return the (possibly unchanged) total.
/// </summary>
public interface IDiscountStrategy
{
    Task<decimal> ApplyAsync(
        decimal currentTotal,
        CreateOrderRequestDto request,
        CancellationToken cancellationToken);
}