using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Entities;
using QuotesApi.Observability;

namespace QuotesApi.Extensions;

/// <summary>
/// Day-11 performance lab. Two endpoints that return the SAME authors→quotes
/// summary, one written badly (N+1 + unindexed scan) and one written well
/// (single GROUP BY that a covering index can satisfy with a seek), plus a
/// one-shot seeder so there is enough data for the difference to be measurable.
///
/// These are intentionally anonymous so a load generator (bombardier/k6) can
/// hammer them without auth. They are a teaching aid, not production endpoints.
/// </summary>
public static class PerfEndpoints
{
    public static IEndpointRouteBuilder MapPerfEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/authors");

        // ── SLOW: 1 + N queries, every child query a clustered-index scan ──────
        // Pattern: load the parents, then loop and load each parent's children
        // with a separate round-trip. With 200 authors that's 201 SQL statements.
        // Because AuthorId is unindexed in the "before" schema, each child query
        // scans the entire Quotes table — so cost is O(authors × total_quotes).
        group.MapGet("/summary", async (AppDbContext db, CancellationToken ct) =>
        {
            using var activity = QuotesApiActivitySource.Source
                .StartActivity("authors.summary.slow");

            // Query #1: all authors.
            var authors = await db.Authors
                .AsNoTracking()
                .OrderBy(a => a.Name)
                .ToListAsync(ct);

            var result = new List<AuthorSummaryDto>(authors.Count);

            // Queries #2..#N+1: one per author. THIS is the N+1.
            foreach (var author in authors)
            {
                var quotes = await db.Quotes
                    .AsNoTracking()
                    .Where(q => q.AuthorId == author.Id && !q.IsDeleted)
                    .ToListAsync(ct);

                var latest = quotes
                    .OrderByDescending(q => q.Id)
                    .Select(q => q.Text)
                    .FirstOrDefault();

                result.Add(new AuthorSummaryDto(author.Id, author.Name, quotes.Count, latest));
            }

            return Results.Ok(result);
        });

        // ── FAST: a single set-based query ────────────────────────────────────
        // One GROUP BY over Quotes joined to Authors. With the covering index
        // IX_Quotes_AuthorId (INCLUDE Text) this is an index seek/stream — no
        // table scan, one round-trip total instead of 1 + N.
        group.MapGet("/summary-fast", async (AppDbContext db, CancellationToken ct) =>
        {
            using var activity = QuotesApiActivitySource.Source
                .StartActivity("authors.summary.fast");

            // Project straight off Authors. The two navigation subqueries
            // (Count + latest Text) are translated by EF into a SINGLE SQL
            // statement (correlated subqueries / OUTER APPLY) — one round-trip
            // instead of the 1 + N the slow endpoint makes. With the covering
            // index on Quotes(AuthorId) these subqueries become seeks.
            var result = await db.Authors
                .AsNoTracking()
                .OrderBy(a => a.Name)
                .Select(a => new AuthorSummaryDto(
                    a.Id,
                    a.Name,
                    a.Quotes.Count(q => !q.IsDeleted),
                    a.Quotes
                        .Where(q => !q.IsDeleted)
                        .OrderByDescending(q => q.Id)
                        .Select(q => q.Text)
                        .FirstOrDefault()))
                .ToListAsync(ct);

            return Results.Ok(result);
        });

        // ── Seeder: POST /api/authors/seed?authors=200&perAuthor=250 ──────────
        // One-time data load. Defaults to 200 authors × 250 quotes = 50,000 rows,
        // enough that a full Quotes scan is clearly slower than an index seek on S2.
        group.MapPost("/seed", async (
            int? authors,
            int? perAuthor,
            AppDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Perf.Seed");
            var authorCount = authors ?? 200;
            var quotesPer = perAuthor ?? 250;

            if (await db.Authors.AnyAsync(ct))
                return Results.Conflict(new { message = "Already seeded. Drop/recreate the DB to reseed." });

            // Turn off automatic change detection — with tens of thousands of
            // inserts, DetectChanges on every Add would dominate the runtime.
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            var sw = Stopwatch.StartNew();

            // 1) Authors first so they get identity values to reference.
            var authorEntities = new List<Author>(authorCount);
            for (var i = 1; i <= authorCount; i++)
                authorEntities.Add(Author.Create($"Author {i:D4}").Value!);

            db.Authors.AddRange(authorEntities);
            await db.SaveChangesAsync(ct);

            // 2) Quotes in batches, clearing the tracker between batches so the
            //    change tracker never holds more than one batch of entities.
            const int batchSize = 5_000;
            var pending = 0;
            var total = 0;

            foreach (var author in authorEntities)
            {
                for (var j = 1; j <= quotesPer; j++)
                {
                    var quote = Quote.Create(
                        author.Name,
                        $"Quote #{j} attributed to {author.Name}. Lorem ipsum dolor sit amet.",
                        ownerId: null,
                        authorId: author.Id).Value!;

                    db.Quotes.Add(quote);

                    if (++pending >= batchSize)
                    {
                        await db.SaveChangesAsync(ct);
                        db.ChangeTracker.Clear();
                        total += pending;
                        pending = 0;
                        logger.LogInformation("Seeded {Total} quotes so far…", total);
                    }
                }
            }

            if (pending > 0)
            {
                await db.SaveChangesAsync(ct);
                total += pending;
            }

            sw.Stop();
            db.ChangeTracker.AutoDetectChangesEnabled = true;

            return Results.Ok(new
            {
                authors = authorCount,
                quotes = total,
                elapsedMs = sw.ElapsedMilliseconds
            });
        });

        return app;
    }
}
