# EF Query Translation, Projection, and Client-Side Evaluation

SQL logging enabled in `Development` only at
[ServiceExtensions.cs:66-79](QuotesApi/Extensions/ServiceExtensions.cs#L66-L79)
via `EnableSensitiveDataLogging()` + `LogTo(Console.WriteLine, ..., LogLevel.Information)`.
Production config is untouched.

All SQL below was captured against SQL Server 2022 (Docker, `localhost,1433`) via
`IQueryable.ToQueryString()`.

---

## A) Original query (entity load)

[QuoteRepository.cs:18-27](QuotesApi/Repositories/QuoteRepository.cs#L18-L27) — pre-change:

```csharp
public async Task<List<Quote>> GetAllAsync(int page, int size, CancellationToken cancellationToken)
{
    return await _context.Quotes
        .Where(q => !q.IsDeleted)
        .Skip((page - 1) * size)
        .Take(size)
        .ToListAsync(cancellationToken);
}
```

## B) Generated SQL — original

```sql
DECLARE @p int = 0;
DECLARE @p1 int = 10;

SELECT [q].[Id], [q].[Author], [q].[IsDeleted], [q].[OwnerId], [q].[Text]
FROM [Quotes] AS [q]
WHERE [q].[IsDeleted] = CAST(0 AS bit)
ORDER BY (SELECT 1)
OFFSET @p ROWS FETCH NEXT @p1 ROWS ONLY
```

Five columns selected (the entire `Quote` row), plus the entities are change-tracked.

## C) Projected query

[QuoteRepository.cs:19-31](QuotesApi/Repositories/QuoteRepository.cs#L19-L31) — current:

```csharp
public async Task<List<QuoteListItemDto>> GetAllAsync(int page, int size, CancellationToken cancellationToken)
{
    return await _context.Quotes
        .Where(q => !q.IsDeleted)
        .OrderBy(q => q.Id)
        .Skip((page - 1) * size)
        .Take(size)
        .Select(q => new QuoteListItemDto(q.Id, q.Author, q.Text))
        .ToListAsync(cancellationToken);
}
```

Minimal DTO at [DTOs/QuoteListItemDto.cs](QuotesApi/DTOs/QuoteListItemDto.cs).

## D) Generated SQL — projected

```sql
DECLARE @p int = 0;
DECLARE @p1 int = 10;

SELECT [q].[Id], [q].[Author], [q].[Text]
FROM [Quotes] AS [q]
WHERE [q].[IsDeleted] = CAST(0 AS bit)
ORDER BY [q].[Id]
OFFSET @p ROWS FETCH NEXT @p1 ROWS ONLY
```

Columns selected: **5 → 3**. `IsDeleted` and `OwnerId` are no longer transferred over
the wire. The projection also opts out of change tracking (projected non-entity types
are never tracked), saving identity-map work.

## E) Client-side evaluation example

A natural follow-up endpoint — "find quotes whose author matches a string,
case-insensitively" — invites this bug:

```csharp
// BAD — string.Equals(..., StringComparison.OrdinalIgnoreCase) cannot be translated
// to T-SQL, so EF gives up at the .Where, pulls the entire Quotes table into memory,
// and filters in the application.
var hits = await _context.Quotes
    .Where(q => string.Equals(q.Author, name, StringComparison.OrdinalIgnoreCase))
    .ToListAsync(cancellationToken);
```

Why it goes client-side: `StringComparison.OrdinalIgnoreCase` has no T-SQL equivalent
that EF's query pipeline can pick. In EF Core 3.0+ the provider throws
`InvalidOperationException` rather than silently degrading — but devs commonly
"fix" this by adding `.AsEnumerable()` before the `.Where`, which loads every row
and then filters in C#. Either way, the table scan happens in the app process.

The same trap appears with `.ToList()` placed before `.Where`, custom C# methods
inside the predicate, or `string.Compare(..., ignoreCase)`.

## F) Fixed version

Push the comparison into SQL with `EF.Functions.Like`, which SQL Server already
treats case-insensitively under the default `_CI_` collation:

```csharp
// GOOD — translates to WHERE [q].[Author] LIKE @name
var hits = await _context.Quotes
    .Where(q => EF.Functions.Like(q.Author, name))
    .ToListAsync(cancellationToken);
```

Captured SQL:

```sql
SELECT [q].[Id], [q].[Author], [q].[IsDeleted], [q].[OwnerId], [q].[Text]
FROM [Quotes] AS [q]
WHERE [q].[Author] LIKE N'einstein'
```

## G) What improved

| Aspect | Before | After |
|---|---|---|
| Columns transferred per row (projection) | 5 | 3 |
| Change tracking overhead (projection) | Yes — every row enters the identity map | No — projected DTOs aren't entities |
| Rows scanned on author search | **all rows**, scanned in C# | only matching rows, found by SQL Server using whatever index is available on `Author` |
| Memory pressure on the API process | full table materialized | one row per match |

The projection change is the bigger long-term win as the `Quotes` table grows wider
(adding columns no longer makes `GET /api/quotes` slower). The client-side fix is
the bigger short-term win — it converts an `O(table_size)` operation into an
`O(matches)` one and lets the database planner use its indexes.
