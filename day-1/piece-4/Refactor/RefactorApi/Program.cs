using Microsoft.EntityFrameworkCore;
using RefactorApi.Data;
using RefactorApi.Models;
using RefactorApi.Repositories;
using RefactorApi.Repositories.Interfaces;
using RefactorApi.Services;
using RefactorApi.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("RefactorDb"));

builder.Services.AddScoped<IOrderRepository, OrderRepository>();

builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (!context.Customers.Any())
    {
        context.Customers.Add(new Customer
        {
            Id = "1",
            Name = "Bhagyashree",
            Email = "test@test.com",
            IsActive = true,
            TotalSpend = 2000
        });

        context.Products.Add(new Product
        {
            Id = "1",
            Name = "Laptop",
            Price = 1000,
            Stock = 10
        });

        context.SaveChanges();
    }
}

app.UseSwagger();

app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}