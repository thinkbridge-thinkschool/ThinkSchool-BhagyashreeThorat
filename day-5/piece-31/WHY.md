# Why a Rich Domain Model for Quote

## What the anemic model failed to protect

The old Quote was a plain data bag with three public setters. Any code anywhere in the
codebase — an endpoint, a background job, a future developer copy-pasting from Stack
Overflow — could write:

```csharp
var q = new Quote { Author = "", Text = new string('x', 50_000) };
await repo.AddAsync(q);
```

No compiler error. No runtime guard. The garbage goes straight into the database.

The endpoint partially compensated by doing its own null checks before construction, but
that guard was invisible to the entity and trivially bypassable: any other call site that
skipped those checks would silently succeed.

## One realistic bug the old model would allow

A developer adds a bulk-import endpoint — "just skip the normal controller validation,
it's an internal endpoint." They construct Quote objects directly from CSV rows and call
`repo.AddAsync`. A CSV with a blank author column, or a 10 000-character quote text, gets
persisted without complaint. The API now returns that row to clients, and client apps
crash parsing the unexpected shape.

With the anemic model, there is no central place to catch this. With the rich model,
`Quote.Create` is the only construction path. It rejects the bad row before it ever
reaches the repository, regardless of which call site triggered the import.

## What the rich model buys us

- **One enforcement point.** `Quote.Create` owns all invariants. Any new call site gets
  validation for free; nobody has to remember to copy the checks.
- **Immutable text.** The private setter makes it structurally impossible to change Text
  after creation — the compiler rejects it before a test can even fail.
- **Soft delete as behaviour.** `quote.Delete()` sets `IsDeleted`; the entity controls
  its own lifecycle state. A caller cannot set `IsDeleted = true` directly because the
  setter is private.
- **Result instead of exception.** Expected validation failures (bad input from a user)
  return a `Result<Quote>` instead of throwing, keeping the exception path for genuinely
  unexpected errors.

## What we deliberately did not add

No CQRS, no MediatR, no event sourcing, no generic validator hierarchy. The model is
richer because it *owns its rules*, not because it has more infrastructure.
