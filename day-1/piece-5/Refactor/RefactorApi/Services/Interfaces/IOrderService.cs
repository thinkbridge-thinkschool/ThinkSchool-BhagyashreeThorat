using RefactorApi.DTOs;

namespace RefactorApi.Services.Interfaces;

public interface IOrderService
{
    Task<CreateOrderResponseDto> CreateOrderAsync(
        CreateOrderRequestDto request,
        CancellationToken cancellationToken);
}