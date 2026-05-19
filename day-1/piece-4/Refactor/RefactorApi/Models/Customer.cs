namespace RefactorApi.Models;

public class Customer
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public bool IsActive { get; set; }
    public decimal TotalSpend { get; set; }
    public DateTime? LastOrderDate { get; set; }
}