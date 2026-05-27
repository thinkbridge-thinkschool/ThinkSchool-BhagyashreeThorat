using System.Diagnostics;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Entities;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using QuotesApi.Options;
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

    // Wire up Azure Key Vault as a configuration source when a vault URI is present.
    // DefaultAzureCredential resolves auth automatically: managed identity in Azure,
    // Azure CLI / VS / VS Code credentials locally (after az login).
    // Secrets override appsettings values, so ApplicationInsights--ConnectionString in
    // the vault replaces any placeholder in appsettings without touching source control.
    var keyVaultUri = builder.Configuration.GetSection(KeyVaultOptions.SectionName)["Uri"];
    if (!string.IsNullOrEmpty(keyVaultUri))
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri(keyVaultUri),
            new DefaultAzureCredential());
    }

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
    builder.Services.AddHealthChecks();

    builder.Services.AddInfrastructure(builder.Configuration);

    var app = builder.Build();

    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapHealthChecks("/health");

    app.MapGet("/", () => Results.Redirect("/swagger"));

    // Push the W3C TraceId into Serilog's LogContext so every log line within
    // this request carries the same TraceId that OpenTelemetry exports.
    // Activity.Current is the OTel root request span (started by
    // AddAspNetCoreInstrumentation before the middleware pipeline runs).
    // Falls back to HttpContext.TraceIdentifier if no OTel activity is present.
    app.Use(async (ctx, next) =>
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
        using (LogContext.PushProperty("TraceId", traceId))
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

    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();

        if (!db.Users.Any(u => u.Email == "admin@quotes.com"))
        {
            db.Users.Add(User.Create("admin@quotes.com", BCrypt.Net.BCrypt.HashPassword("Password123!")));
            db.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Database migration skipped — no database reachable at startup");
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
