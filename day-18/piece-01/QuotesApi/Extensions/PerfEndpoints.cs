using System.Diagnostics;
using Dapper;
using Microsoft.EntityFrameworkCore;

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
        MapBulkListEndpoints(app);

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

        // ── DAPPER: same summary, hand-written SQL (Day-12 EF-vs-Dapper lab) ──
        // Identical contract to /summary: same AuthorSummaryDto shape, same
        // filtering (IsDeleted = 0), same ordering (by Name), same two
        // per-author aggregations (Count + latest Text). The ONLY difference is
        // the data-access mechanism: Dapper maps the result of a hand-written
        // SQL string straight onto the DTO record, with no EF model, no change
        // tracker, and no LINQ→SQL translation step.
        //
        // We borrow the connection the DbContext already owns
        // (db.Database.GetDbConnection()) so this reuses the SAME connection
        // string and pooling as EF — no new DI wiring, no second config source.
        // Dapper just executes against that ADO.NET connection.
        //
        // The SQL below is the literal text Dapper sends — there is no
        // translation layer, so "the generated SQL" IS this string. It mirrors
        // what EF produces for /summary: two correlated subqueries per author,
        // both satisfied by the covering index IX_Quotes_AuthorId.
        group.MapGet("/summary-dapper", async (AppDbContext db, CancellationToken ct) =>
        {
            using var activity = QuotesApiActivitySource.Source
                .StartActivity("authors.summary.dapper");

            const string sql = """
                SELECT
                    a.Id   AS AuthorId,
                    a.Name AS Name,
                    (SELECT COUNT(*)
                       FROM Quotes q
                      WHERE q.AuthorId = a.Id AND q.IsDeleted = 0) AS QuoteCount,
                    (SELECT TOP (1) q.[Text]
                       FROM Quotes q
                      WHERE q.AuthorId = a.Id AND q.IsDeleted = 0
                      ORDER BY q.Id DESC) AS LatestQuote
                FROM Authors a
                ORDER BY a.Name;
                """;

            var connection = db.Database.GetDbConnection();

            // Dapper maps each column to the AuthorSummaryDto constructor
            // parameter of the same name (case-insensitive). CommandDefinition
            // carries the CancellationToken so the read honours request abort.
            var result = await connection.QueryAsync<AuthorSummaryDto>(
                new CommandDefinition(sql, cancellationToken: ct));

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

    /// <summary>
    /// Day-12 EF-vs-Dapper lab — the RIGHT experiment for measuring ORM overhead.
    ///
    /// Both endpoints return the SAME large, flat list of QuoteListItemDto with the
    /// SAME trivial filter (IsDeleted = 0) and the SAME ordering (by the clustered
    /// PK Id). The SQL is near-identical and cheap — a clustered range scan that
    /// returns the first @take rows — so SQL Server cost is flat and equal for both.
    ///
    /// The ONLY thing that differs is the materialization pipeline:
    ///   • EF builds a query, compiles a shaper, and runs it per row (even with
    ///     AsNoTracking + projection there is shaper + reader overhead per row).
    ///   • Dapper reads the data reader straight into the record with a generated
    ///     row parser — no model, no expression tree, no query pipeline.
    ///
    /// With thousands of rows per request, repeated, that per-row delta is what the
    /// benchmark measures — ORM overhead, not the database.
    /// </summary>
    private static void MapBulkListEndpoints(IEndpointRouteBuilder app)
    {
        var bulk = app.MapGroup("/api/quotes-bulk");

        // ── EF: AsNoTracking projection (EF's best case for reads) ────────────
        bulk.MapGet("/ef", async (int? take, AppDbContext db, CancellationToken ct) =>
        {
            using var activity = QuotesApiActivitySource.Source.StartActivity("quotes.bulk.ef");

            var n = take ?? 5000;

            var result = await db.Quotes
                .AsNoTracking()
                .Where(q => !q.IsDeleted)
                .OrderBy(q => q.Id)
                .Take(n)
                .Select(q => new QuoteListItemDto(q.Id, q.Author, q.Text))
                .ToListAsync(ct);

            return Results.Ok(result);
        });

        // ── DAPPER: same shape, hand-written SQL, direct row parsing ──────────
        // Borrows the DbContext's own connection so the connection string and
        // pool are identical to EF — the only variable is the materialization.
        bulk.MapGet("/dapper", async (int? take, AppDbContext db, CancellationToken ct) =>
        {
            using var activity = QuotesApiActivitySource.Source.StartActivity("quotes.bulk.dapper");

            var n = take ?? 5000;

            const string sql = """
                SELECT TOP (@take) Id, Author, [Text]
                FROM Quotes
                WHERE IsDeleted = 0
                ORDER BY Id
                """;

            var connection = db.Database.GetDbConnection();

            // Dapper maps Id/Author/Text columns onto the QuoteListItemDto record
            // constructor by name. @take is a real SQL parameter (parameterized).
            var result = await connection.QueryAsync<QuoteListItemDto>(
                new CommandDefinition(sql, new { take = n }, cancellationToken: ct));

            return Results.Ok(result);
        });
    }
}
