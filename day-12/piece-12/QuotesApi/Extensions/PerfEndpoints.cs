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

        // ── FIXED (Day-11 p11): single set-based query, no N+1 ────────────────
        // WAS: load all authors, then loop and fire one extra query PER author to
        // count + fetch their quotes — 1 + 200 = 201 round-trips, and with an
        // unindexed AuthorId each child query scanned all 50k Quotes rows.
        //
        // NOW: project straight off Authors. EF translates the two navigation
        // sub-aggregations (Count + latest Text) into ONE SQL statement using
        // correlated subqueries / OUTER APPLY — so it is a single round-trip
        // instead of 1 + N. This is WHY the N+1 disappears: there is no per-row
        // client-side loop issuing queries; the set-based work happens server-side
        // in one statement. Backed by the covering index IX_Quotes_AuthorId
        // (AuthorId) INCLUDE (IsDeleted, Text), each subquery is an index seek that
        // reads only covered columns (no key lookup) — see Scripts/add-covering-index.sql.
        group.MapGet("/summary", async (AppDbContext db, CancellationToken ct) =>
        {
            using var activity = QuotesApiActivitySource.Source
                .StartActivity("authors.summary");

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
