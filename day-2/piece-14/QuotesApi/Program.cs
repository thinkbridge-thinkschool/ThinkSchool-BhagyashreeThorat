using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Entities;
using QuotesApi.Extensions;
using QuotesApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger"));

app.UseMiddleware<ExceptionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    if (!db.Users.Any(u => u.Email == "admin@quotes.com"))
    {
        db.Users.Add(User.Create("admin@quotes.com", BCrypt.Net.BCrypt.HashPassword("Password123!")));
        db.SaveChanges();
    }
}

app.MapAuthEndpoints();
app.MapQuoteEndpoints();

app.Run();

// Exposes the implicit Program class so integration tests can reference it via WebApplicationFactory<Program>
public partial class Program { }
