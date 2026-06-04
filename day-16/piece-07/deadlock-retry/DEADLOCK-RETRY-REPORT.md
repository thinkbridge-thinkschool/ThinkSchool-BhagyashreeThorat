# Deadlock Retry — Reproduce, Capture Evidence, Real Fix, Verify

**Environment (existing, unchanged):** SQL Server 2022 (`piece-6-sqlserver-1`, `localhost,1433`),
database `QuotesDb`, existing table `dbo.DeadlockDemo` (PK clustered, rows `Id=1`, `Id=2`).
No schema redesign, no app code changes.

**Two resources:** row `Id=1` and row `Id=2` of `dbo.DeadlockDemo`.

---

## Addressing the two mentor points

| Feedback | What was wrong before | What this submission does |
|---|---|---|
| 1. "Victim message alone is insufficient — need deadlock graph / lock evidence" | Only the Msg 1205 text was shown | Captured the **full deadlock graph** from the `system_health` Extended Events session (`xml_deadlock_report`) **and** the **trace flag 1222** error-log entry — both show resources, owner/waiter locking relationships, the wait chain, and the victim. |
| 2. "Previous fix only changed timing — need real prevention" | The old "fix" just lowered `WAITFOR` from 10s to 5s | Real fix = **consistent lock ordering** (always lock ascending `Id`) + justified `UPDLOCK, ROWLOCK` hints. The **same `WAITFOR '00:00:04'`** that caused the deadlock is kept in the fixed scripts, proving the cure is the ordering, not the delay. |

---

## A) Session A — repro  → `A_session_repro.sql`
Locks `Id=1` then `Id=2`.

## B) Session B — repro  → `B_session_repro.sql`
Locks `Id=2` then `Id=1`  (opposite order → circular wait).

Run concurrently → **deadlock every time.** Observed:
```
A: locked Id=1
B: locked Id=2
A: requesting Id=2 ...        B: requesting Id=1 ...
Msg 1205 ... Process ID 53 ... was chosen as the deadlock victim.
B: committed OK
```

## C) Deadlock graph / lock evidence
- `evidence/deadlock_graph.xml` — raw `xml_deadlock_report` from `system_health`.
- `evidence/deadlock_graph_annotated.txt` — trimmed, annotated version (below).
- `evidence/tf1222_errorlog.txt` — trace flag 1222 copy from the SQL error log.

Key relationships from the graph:

```
victim-list:  process980423088  (spid 53 = Session A)

resource-list (both on QuotesDb.dbo.DeadlockDemo, index PK__Deadlock__3214EC07):
  KEY for Id=2 :  owner = B (spid52, X)   waiter = A (spid53, X, wait)
  KEY for Id=1 :  owner = A (spid53, X)   waiter = B (spid52, X, wait)

WAIT CHAIN (circular):
  A holds Id=1 ─waits→ Id=2 (held by B)
  B holds Id=2 ─waits→ Id=1 (held by A)   → cycle → A killed (Msg 1205)
```

## D) Fixed scripts
- `A_session_fixed.sql` — locks `Id=1` then `Id=2`.
- `B_session_fixed.sql` — locks **`Id=1` then `Id=2`** (was 2→1). This single change removes the opposite order.
- Both use `WITH (UPDLOCK, ROWLOCK)`: `UPDLOCK` takes the update lock up front (no S→X upgrade deadlocks if the pattern ever becomes read-then-write); `ROWLOCK` keeps locking at row grain (no escalation widening the contended resource).

## E) Evidence the fix works  → `evidence/fix_verify.out`
Ran A_fixed + B_fixed concurrently for **5 rounds (10 transactions)** with the *same* 4s delay:
```
Transactions run: 10 (5 rounds x 2 concurrent).  Deadlocks(Msg 1205)=0   committedOK=10
```
Every round: both `A(fixed): committed OK` and `B(fixed): committed OK`.
Cross-check: `system_health` still holds exactly **1** deadlock event (the repro at 13:12:04);
**no new deadlock** was recorded during verification. (Trace flag 1222 disabled afterward — env restored.)

## F) Why the fix works (one line)
Both transactions now acquire the rows in the **same ascending-Id order**, so they queue behind each other on the first row instead of each holding one row and demanding the other — the circular wait can never form, so no deadlock is possible regardless of timing.
