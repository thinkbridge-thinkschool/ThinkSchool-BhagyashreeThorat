// Models.cs  -- "just put them all in one file for now" -- Tim
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Net.Mail;

namespace LegacyShop
{
    // ---- Entities ----

    public class Customer
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public bool IsActive { get; set; }
        public decimal TotalSpend { get; set; }
        public DateTime? LastOrderDate { get; set; }
    }

    public class Product
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
    }

    public class Order
    {
        public string Id { get; set; }
        public string CustomerId { get; set; }
        public string Status { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Shipping { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
        public string CouponCode { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<OrderItem> Items { get; set; }
    }

    public class OrderItem
    {
        public string Id { get; set; }
        public string OrderId { get; set; }
        public string ProductId { get; set; }
        public string ProductName { get; set; }  // denormalised copy
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class Coupon
    {
        public string Id { get; set; }
        public string Code { get; set; }
        public decimal DiscountPercent { get; set; }
        public int MaxUsage { get; set; }
        public int UsageCount { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    // ---- DbContext ----

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Customer> Customers { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Coupon> Coupons { get; set; }
    }

    // ---- Static email helper from a dark time ----

    public static class EmailHelper
    {
        // hardcoded SMTP -- credentials in code, committed to git in 2021
        public static void SendOrderConfirmation(string toEmail, string orderId, decimal total)
        {
            var client = new SmtpClient("smtp.legacyshop.internal", 25);
            var msg = new MailMessage("no-reply@legacyshop.com", toEmail);
            msg.Subject = "Your order " + orderId;
            msg.Body = "Thanks! You owe us $" + total;
            client.Send(msg); // sync, blocking
        }
    }
}
