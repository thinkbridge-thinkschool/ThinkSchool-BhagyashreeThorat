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
using QuotesApi.Options;
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
        // ── Options registration ──────────────────────────────────────────────
        // Each options class is bound from its named section, validated with
        // data annotations, and verified at startup so the app refuses to start
        // rather than crashing at the first request that reads a bad value.
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EntraOptions>()
            .BindConfiguration(EntraOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OpenTelemetryOptions>()
            .BindConfiguration(OpenTelemetryOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Infrastructure services ───────────────────────────────────────────
        services.AddSingleton<IClock, SystemClock>();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is required.")));

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        // ── Read auth config values from the typed sections ───────────────────
        // These values are needed at registration time (before DI is built) so
        // we read them directly from the configuration. The options framework
        // will still validate them at startup via ValidateOnStart above.
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var signingKey = jwtSection["SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is required. Set it via dotnet user-secrets in development or an environment variable / Key Vault in production.");
        var jwtIssuer = jwtSection["Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer is required.");
        var jwtAudience = jwtSection["Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience is required.");

        var entraSection = configuration.GetSection(EntraOptions.SectionName);
        var tenantId = entraSection["TenantId"]
            ?? throw new InvalidOperationException("Entra:TenantId is required.");
        var entraAudience = entraSection["Audience"]
            ?? throw new InvalidOperationException("Entra:Audience is required.");

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

            // ── Scheme 1: internal HMAC-signed JWTs ───────────────────────────
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
                    ValidIssuer              = jwtIssuer,
                    ValidAudience            = jwtAudience,
                    IssuerSigningKey         = new SymmetricSecurityKey(
                                                  Encoding.UTF8.GetBytes(signingKey)),
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
            options.AddPolicy(AuthorizationPolicies.CanEditQuotes,
                p => p.RequireClaim(AppClaimTypes.Scope, "quotes.write"));

            // ── Claim + custom requirement ────────────────────────────────────
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
        var otelSection = configuration.GetSection(OpenTelemetryOptions.SectionName);
        var serviceName = otelSection["ServiceName"] ?? "QuotesApi";

        var otelBuilder = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: "1.0.0"))
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
                        var endpoint = otelSection["OtlpEndpoint"];
                        if (!string.IsNullOrEmpty(endpoint))
                            opts.Endpoint = new Uri(endpoint);
                    });
            });

        // Only wire up Azure Monitor when a connection string is available.
        // Skipped in local dev so the app starts without an App Insights resource.
        var appInsightsCs =
            configuration["ApplicationInsights:ConnectionString"]
            ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

        if (!string.IsNullOrEmpty(appInsightsCs))
            otelBuilder.UseAzureMonitor();

        return services;
    }
}
