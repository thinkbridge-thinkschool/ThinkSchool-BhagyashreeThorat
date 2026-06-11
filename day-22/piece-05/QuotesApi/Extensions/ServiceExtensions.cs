using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;
using QuotesApi.Authorization;
using QuotesApi.Infrastructure;
using QuotesApi.Options;

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
        IConfiguration configuration,
        IHostEnvironment environment)
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

        // ── Background job processing ─────────────────────────────────────────
        // The queue is a singleton (shared by request threads and the drainer);
        // the BackgroundService is hosted so the host starts/stops it with the app.
        services.AddSingleton<BackgroundJobs.IBackgroundTaskQueue>(_ =>
            new BackgroundJobs.BackgroundTaskQueue(capacity: 100));
        services.AddHostedService<BackgroundJobs.QueuedHostedService>();

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is required."));

            // Dev-only EF diagnostics: emit generated SQL to console with parameter
            // values inlined. EnableSensitiveDataLogging writes parameter literals into
            // the log — never enable in production (PII / credentials leak risk).
            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();

                // EF echoes every generated SQL command to the console in dev so
                // you can read it. That is one Console.WriteLine PER QUERY — a cost
                // Dapper never pays, since it doesn't go through EF's logger. When
                // benchmarking EF-vs-Dapper we silence it with EF_SQL_CONSOLE=false
                // so the timing reflects ORM materialization, not console I/O.
                var sqlConsole = Environment.GetEnvironmentVariable("EF_SQL_CONSOLE");
                if (!string.Equals(sqlConsole, "false", StringComparison.OrdinalIgnoreCase))
                {
                    options.LogTo(Console.WriteLine, new[] { DbLoggerCategory.Database.Command.Name }, LogLevel.Information);
                }
            }
        });

        // ── Day-21: HybridCache (L1 in-memory + L2 Redis) ─────────────────────
        // HybridCache unifies two tiers behind one GetOrCreateAsync call:
        //   • L1 — a process-local in-memory cache (fastest, but per-instance).
        //   • L2 — Redis, shared across instances and surviving restarts.
        // A read checks L1, then L2, then runs the factory (the DB query) and
        // back-fills both tiers.
        //
        // The headline feature for this lab is STAMPEDE PROTECTION: when many
        // requests miss the same key at once, HybridCache runs the factory ONCE
        // and hands the single result to every waiter — instead of letting N
        // concurrent requests each stampede the database with the same query.
        //
        // Registering an IDistributedCache (Redis) is what promotes HybridCache
        // from L1-only to the full two-tier cache. If Redis is absent it still
        // works as an in-memory cache, so the app degrades gracefully.
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "quotes:";
            });
        }

        services.AddHybridCache(options =>
        {
            // Default per-entry lifetimes. Expiration = total time-to-live in L2;
            // LocalCacheExpiration = how long L1 may serve before it must re-check
            // L2. Individual GetOrCreateAsync calls can override these.
            options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(5),
            };
        });

        // Shared tally so the cached endpoint can prove its hit rate / DB-load drop.
        services.AddSingleton<CacheMetrics>();

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();

        // ── CQRS-lite: Quote read/write paths ────────────────────────────────
        // Same DB, same app, different shapes. The command handler owns the
        // normalized write; the query handler owns the projection-shaped read.
        services.AddScoped<CreateQuoteHandler>();
        services.AddScoped<GetQuotesHandler>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        // ── Outbound HttpClient + Polly resilience pipeline ───────────────────
        // Named client "my-service" — resolved with IHttpClientFactory.CreateClient("my-service").
        // Its BaseAddress points at our own FlakyUpstreamController (over localhost) so the
        // whole lab is self-contained and the failure mode is toggleable on demand.
        //
        // Pipeline order (outer → inner; each strategy wraps the next):
        //   1. Bulkhead  (concurrency limiter) — caps in-flight calls, sheds the rest
        //   2. Retry     — re-issues IDEMPOTENT transient failures with exp. backoff
        //   3. Circuit breaker — trips after sustained failures, fails fast while open
        //   4. Timeout   — innermost, applied PER ATTEMPT (so each retry gets a fresh 10s)
        var upstreamBaseUrl = configuration["Upstream:BaseUrl"] ?? "http://localhost:5005";

        services.AddHttpClient("my-service", client =>
            {
                client.BaseAddress = new Uri(upstreamBaseUrl);
            })
            .AddResilienceHandler("default", (b, context) =>
            {
                // Resolve a logger from DI so every resilience event is observable.
                var logger = context.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("QuotesApi.Resilience.MyService");

                // ── 1. Bulkhead isolation (outermost) ─────────────────────────
                // A concurrency limiter is Polly v8's bulkhead: at most PermitLimit
                // calls run the inner pipeline at once; with QueueLimit = 0 any
                // excess is rejected immediately (RateLimiterRejectedException)
                // instead of piling up and exhausting threads/sockets.
                b.AddRateLimiter(new RateLimiterStrategyOptions
                {
                    DefaultRateLimiterOptions = new ConcurrencyLimiterOptions
                    {
                        PermitLimit = 3,
                        QueueLimit = 0,
                    },
                    OnRejected = args =>
                    {
                        logger.LogError(
                            "[BULKHEAD] REJECTED — concurrency limit (3) reached, request shed to protect the dependency");
                        return ValueTask.CompletedTask;
                    },
                });

                // ── 2. Retry with exponential backoff (IDEMPOTENT ONLY) ───────
                b.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,

                    // Idempotency gate: retrying a non-idempotent call (e.g. POST)
                    // risks double side-effects, so we only retry safe/idempotent
                    // methods AND only when the failure is transient.
                    ShouldHandle = args =>
                    {
                        var outcome = args.Outcome;
                        var method = outcome.Result?.RequestMessage?.Method;

                        // If we know the method and it is NOT idempotent, never retry.
                        if (method is not null && !IsIdempotent(method))
                            return ValueTask.FromResult(false);

                        var transient =
                            outcome.Exception is HttpRequestException or TimeoutRejectedException
                            || (outcome.Result is { } resp &&
                                ((int)resp.StatusCode >= 500
                                 || resp.StatusCode == HttpStatusCode.RequestTimeout));

                        return ValueTask.FromResult(transient);
                    },

                    OnRetry = args =>
                    {
                        var outcome = args.Outcome;
                        var statusCode = outcome.Result?.StatusCode;
                        var exception = outcome.Exception;
                        var requestUri = outcome.Result?.RequestMessage?.RequestUri?.ToString() ?? "n/a";

                        logger.LogWarning(
                            exception,
                            "[RETRY] attempt {AttemptNumber} for {RequestUri} after {DelayMs:F0}ms — status: {StatusCode}, error: {ErrorMessage}",
                            args.AttemptNumber + 1,
                            requestUri,
                            args.RetryDelay.TotalMilliseconds,
                            statusCode?.ToString() ?? "n/a",
                            exception?.Message ?? "n/a");

                        return ValueTask.CompletedTask;
                    },
                });

                // ── 3. Circuit breaker ────────────────────────────────────────
                // Opens when ≥50% of at least 5 calls in a 30s window fail; while
                // open every call fails fast for 15s, then ONE trial call probes
                // recovery (half-open). Success closes it; failure re-opens it.
                b.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = 5,
                    BreakDuration = TimeSpan.FromSeconds(15),

                    OnOpened = args =>
                    {
                        logger.LogError(
                            "[CIRCUIT] OPENED for {BreakDurationMs:F0}ms after sustained failures (ratio breached) — calls now fail fast",
                            args.BreakDuration.TotalMilliseconds);
                        return ValueTask.CompletedTask;
                    },
                    OnHalfOpened = args =>
                    {
                        logger.LogWarning(
                            "[CIRCUIT] HALF-OPENED — break elapsed, allowing one trial call to probe recovery");
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = args =>
                    {
                        logger.LogInformation(
                            "[CIRCUIT] CLOSED — trial call succeeded, dependency recovered, normal traffic resumed");
                        return ValueTask.CompletedTask;
                    },
                });

                // ── 4. Timeout (innermost, per attempt) ───────────────────────
                b.AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    OnTimeout = args =>
                    {
                        logger.LogError(
                            "[TIMEOUT] attempt exceeded {TimeoutMs:F0}ms — request cancelled",
                            args.Timeout.TotalMilliseconds);
                        return ValueTask.CompletedTask;
                    },
                });
            });

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

    // Safe/idempotent HTTP methods may be retried without risking duplicate
    // side-effects. POST and PATCH are intentionally excluded.
    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get
        || method == HttpMethod.Head
        || method == HttpMethod.Put
        || method == HttpMethod.Delete
        || method == HttpMethod.Options
        || method == HttpMethod.Trace;
}
