namespace RefactorApi.DTOs;

public class CreateOrderResponseDto
{
    public string OrderId { get; set; }

    public decimal Total { get; set; }

    public string Status { get; set; }

    public string Message { get; set; }
}