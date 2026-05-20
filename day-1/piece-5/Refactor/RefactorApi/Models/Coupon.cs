namespace RefactorApi.Models;

public class Coupon
{
    public string Id { get; set; }

    public string Code { get; set; }

    public decimal DiscountPercent { get; set; }

    public int MaxUsage { get; set; }

    public int UsageCount { get; set; }

    public DateTime ExpiresAt { get; set; }
}