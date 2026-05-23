using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi.Abstractions;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.Observability;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class ServiceExtensions
{
    // Scheme names are public so controllers/policies can reference them without magic strings.
    public const string InternalJwtScheme = "InternalJwt";
    public const string EntraScheme = "EntraId";

    // HybridBearer is the default scheme registered with ASP.NET Core.
    // It acts as a router: it inspects the incoming token's issuer and
    // forwards the authentication attempt to whichever real scheme owns that issuer.
    // This means [Authorize] (no scheme specified) works for BOTH internal and Entra tokens.
    private const string HybridScheme = "HybridBearer";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default")
                ?? "Server=(localdb)\\mssqllocaldb;Database=QuotesDb;Trusted_Connection=True;"));

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        // ── Internal JWT config ───────────────────────────────────────────────
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is required in configuration.");

        // ── Entra ID config ───────────────────────────────────────────────────
        var tenantId = configuration["Entra:TenantId"]
            ?? throw new InvalidOperationException("Entra:TenantId is required in configuration.");

        var entraAudience = configuration["Entra:Audience"]
            ?? throw new InvalidOperationException("Entra:Audience is required in configuration.");

        var entraAuthority = $"https://login.microsoftonline.com/{tenantId}/v2.0";

        // ── Hybrid authentication ─────────────────────────────────────────────
        // AddPolicyScheme creates a lightweight forwarding scheme (HybridBearer)
        // that becomes the application default.  Its ForwardDefaultSelector runs
        // before any real validation: it reads the JWT issuer from the raw header
        // (no signature check needed here — that happens inside the forwarded scheme)
        // and routes to the correct handler.
        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = HybridScheme;
                options.DefaultChallengeScheme    = HybridScheme;
                options.DefaultForbidScheme       = HybridScheme;
            })
            .AddPolicyScheme(HybridScheme, displayName: "Internal JWT or Entra ID", configureOptions: options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var authHeader = context.Request.Headers["Authorization"]
                        .FirstOrDefault();

                    if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var rawToken = authHeader["Bearer ".Length..].Trim();
                        try
                        {
                            var handler = new JwtSecurityTokenHandler();
                            if (handler.CanReadToken(rawToken))
                            {
                                var jwt = handler.ReadJwtToken(rawToken);
                                // Entra v2.0 tokens always carry an issuer of the form
                                // https://login.microsoftonline.com/{tenantId}/v2.0
                                if (jwt.Issuer.Contains("login.microsoftonline.com",
                                        StringComparison.OrdinalIgnoreCase))
                                    return EntraScheme;
                            }
                        }
                        catch
                        {
                            // Malformed token — fall through to internal scheme so
                            // the real handler can return the proper 401.
                        }
                    }

                    return InternalJwtScheme;
                };
            })

            // ── Scheme 1: internal HMAC-signed JWTs (existing, unchanged) ─────
            .AddJwtBearer(InternalJwtScheme, options =>
            {
                // Preserve claim names as-is (sub stays "sub", not mapped to
                // ClaimTypes.NameIdentifier).  Required so FindFirstValue("sub")
                // works in controllers and authorization handlers.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidateAudience         = true,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer              = configuration["Jwt:Issuer"],
                    ValidAudience            = configuration["Jwt:Audience"],
                    IssuerSigningKey         = new SymmetricSecurityKey(
                                                  Encoding.UTF8.GetBytes(secret)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            })

            // ── Scheme 2: Entra ID (Azure AD) RS256-signed JWTs ──────────────
            // Setting Authority tells the middleware to fetch the OpenID Connect
            // discovery document from Azure AD and use its published signing keys.
            // No hard-coded public key is needed; key rotation is automatic.
            .AddJwtBearer(EntraScheme, options =>
            {
                options.Authority = entraAuthority;
                options.Audience  = entraAudience;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer   = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    // The issuer from Entra v2.0 includes the tenant ID.
                    ValidIssuer      = entraAuthority,
                    // Clock skew of 5 minutes matches Azure AD's own tolerance.
                    ClockSkew        = TimeSpan.FromMinutes(5),
                };
            });

        services.AddAuthorization(options =>
        {
            // ── Claim-based policy ────────────────────────────────────────────
            // "can-edit-quotes": caller must present a JWT that contains
            //   scope = "quotes.write".  No role check — roles are implementation
            //   details; policies represent business rules.
            options.AddPolicy(AuthorizationPolicies.CanEditQuotes,
                p => p.RequireClaim(AppClaimTypes.Scope, "quotes.write"));

            // ── Claim + custom requirement ────────────────────────────────────
            // "can-delete-own-quote": must have the write scope AND must own the
            //   specific quote being deleted (enforced by CanDeleteOwnQuoteHandler).
            options.AddPolicy(AuthorizationPolicies.CanDeleteOwnQuote, p =>
            {
                p.RequireClaim(AppClaimTypes.Scope, "quotes.write");
                p.AddRequirements(new CanDeleteOwnQuoteRequirement());
            });
        });

        // Register the custom handler in DI.  Scoped is correct because it depends
        // on IQuoteRepository which is also scoped (one instance per HTTP request).
        services.AddScoped<IAuthorizationHandler, CanDeleteOwnQuoteHandler>();

        // ── OpenTelemetry distributed tracing + Azure Monitor export ─────────
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: configuration["OpenTelemetry:ServiceName"] ?? "QuotesApi",
                    serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(opts =>
                    {
                        // Exclude swagger UI paths — they produce noise with no diagnostic value.
                        opts.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/swagger");
                    })
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource(QuotesApiActivitySource.Name)
                    .AddOtlpExporter(opts =>
                    {
                        var endpoint = configuration["OpenTelemetry:OtlpEndpoint"];
                        if (!string.IsNullOrEmpty(endpoint))
                            opts.Endpoint = new Uri(endpoint);
                        // If no endpoint is configured, the SDK uses OTEL_EXPORTER_OTLP_ENDPOINT
                        // env var, or falls back silently (no crash on export failure).
                    });
            })
            // Exports traces, metrics, and logs to Azure Application Insights.
            // Reads APPLICATIONINSIGHTS_CONNECTION_STRING env var or
            // ApplicationInsights:ConnectionString from IConfiguration (Key Vault in prod,
            // user secrets locally). No-op when the connection string is absent.
            .UseAzureMonitor();

        return services;
    }
}
