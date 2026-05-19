namespace RefactorApi.Models;

public class OrderItem
{
    public string Id { get; set; }
    public string OrderId { get; set; }
    public string ProductId { get; set; }
    public string ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}