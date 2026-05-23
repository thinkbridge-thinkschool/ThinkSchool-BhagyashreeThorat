using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Entities;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using Serilog;
using Serilog.Context;

// Bootstrap logger handles startup errors before the host and its DI-configured
// logger are available. Replaced by the fully-configured logger after host build.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [BOOT] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting QuotesApi");

    var builder = WebApplication.CreateBuilder(args);

    // Replace ASP.NET Core's default logging with Serilog.
    // ReadFrom.Configuration reads sink and level config from appsettings.json ("Serilog" section).
    // ReadFrom.Services wires up any ILogEventEnricher registered in DI.
    // Enrichers here run for every log event regardless of environment.
    builder.Host.UseSerilog((context, services, config) =>
    {
        config
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProcessId();
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddControllers();

    builder.Services.AddInfrastructure(builder.Configuration);

    var app = builder.Build();

    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapGet("/", () => Results.Redirect("/swagger"));

    // Push HttpContext.TraceIdentifier into the Serilog LogContext so every log
    // line produced within this request carries the same TraceId property.
    // Must be registered before UseSerilogRequestLogging so the property is
    // present when Serilog writes the per-request summary line.
    app.Use(async (ctx, next) =>
    {
        using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
        {
            await next();
        }
    });

    // Emit one structured log line per request (method, path, status, elapsed).
    // Placed after the correlation middleware so TraceId is in the log context.
    app.UseSerilogRequestLogging();

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

    app.MapControllers();
    app.MapAuthEndpoints();
    app.MapQuoteEndpoints();

    app.Run();

    Log.Information("QuotesApi stopped cleanly");
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "QuotesApi terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Exposes the implicit Program class so integration tests can reference it via WebApplicationFactory<Program>
public partial class Program { }
