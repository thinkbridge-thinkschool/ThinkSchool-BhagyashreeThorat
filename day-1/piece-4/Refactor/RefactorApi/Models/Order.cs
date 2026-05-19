namespace RefactorApi.Models;

public class Order
{
    public string Id { get; set; }
    public string CustomerId { get; set; }
    public string Status { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string? CouponCode { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<OrderItem> Items { get; set; } = new();
}