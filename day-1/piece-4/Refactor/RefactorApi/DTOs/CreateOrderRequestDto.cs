namespace RefactorApi.DTOs;

public class CreateOrderRequestDto
{
    public string CustomerId { get; set; }
    public List<OrderItemRequestDto> Items { get; set; } = new();
    public string? CouponCode { get; set; }
}