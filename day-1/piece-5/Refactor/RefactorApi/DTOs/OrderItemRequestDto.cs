namespace RefactorApi.DTOs;

public class OrderItemRequestDto
{
    public string ProductId { get; set; }
    public int Quantity { get; set; }
}