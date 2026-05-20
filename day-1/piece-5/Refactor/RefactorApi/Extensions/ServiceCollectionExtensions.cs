using RefactorApi.Services;
using RefactorApi.Services.DiscountStrategies;
using RefactorApi.Services.Interfaces;

namespace RefactorApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrderServices(this IServiceCollection services)
    {
        services.AddScoped<IOrderService, OrderService>();

        // Strategies are resolved by DI as IEnumerable<IDiscountStrategy> in OrderService.
        // Register additional strategies here; they run in registration order.
        services.AddScoped<IDiscountStrategy, CouponDiscountStrategy>();

        return services;
    }
}