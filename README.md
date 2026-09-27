# High-volume reconciliation batch worker — .NET

Every night, banks, credit bureaus (CRIF), utilities and logistics hubs ingest **millions of rows**
of settlement or transaction records from flat files, reconcile them, and quarantine anomalies. The
junior version reads the file into a `List` and calls `SaveChanges()` per row — it exhausts memory
and takes hours. This is the version that **streams at flat memory and writes in bulk**.

A **.NET 10** worker that ingests a large delimited feed at a small, constant memory footprint —
`IAsyncEnumerable` streaming reader → bounded `System.Threading.Channels` → micro-batch
`SqlBulkCopy` — with a dead-letter table, file-level idempotency, restartable bulk writes, and a
BenchmarkDotNet number behind the design.

Target market: banking / credit risk (CRIF), utilities, and large logistics hubs (Milano, Bologna,
Verona).

> Follows the shared [engineering standards](../ENGINEERING-STANDARDS.md). The headline is a measured
> [benchmark](docs/BENCHMARK.md), not a feature list.

## How it works

```
file ──FileStreamer (IAsyncEnumerable, one line at a time)──▶ SpanCsvParser (zero-alloc)
                                                                  │  valid            │ malformed
                                                                  ▼                   ▼
                                              bounded Channel<SettlementRecord>   rejected_row (dead-letter)
                                                                  │  (backpressure)
                                          consumer drains in micro-batches of N
                                                                  ▼
                                   SqlBulkCopy → #stage → INSERT … WHERE NOT EXISTS (idempotent)
```

| Project | Role |
|---------|------|
| `Reconciliation.Core` | The streaming reader, the **zero-allocation `Span<char>` CSV parser** (with reason codes), the deterministic sample generator. |
| `Reconciliation.Data` | EF Core (SQL Server) entities + **migrations**, and the `SqlBulkCopy` writer (staging + set-based idempotent insert). |
| `Reconciliation.Worker` | The `Channel` pipeline, file-level idempotency (`sha256` ledger), dead-lettering, and a **Quartz** nightly schedule (or `--file` one-shot). |
| `benchmarks/` | BenchmarkDotNet: EF `AddRange` vs `SqlBulkCopy`. |
| `tests/` | Span-parser unit tests + Testcontainers **SQL Server** integration (ingest, dead-letter, idempotent re-run). |

## The four things this gets right

1. **Flat memory at any file size.** The reader is `IAsyncEnumerable` and the pipeline is a *bounded*
   `Channel`: if the DB writer slows down the reader blocks, so the whole file is never buffered.
2. **Bulk writes, not row-by-row.** `SqlBulkCopy` measured **~12× faster** than EF `AddRange`
   ([benchmark](docs/BENCHMARK.md)); staged behind `INSERT … WHERE NOT EXISTS` so it is **idempotent
   and restartable** on the natural key.
3. **Nothing is lost silently.** Malformed rows go to a `rejected_row` **dead-letter** table with a
   specific reason code; the run's read / imported / skipped counts are stamped on the ledger.
4. **A re-drop is a no-op.** File-level idempotency by `sha256`; a byte-identical file that already
   completed is skipped, a changed one reprocessed, a failed one safely re-run.

## What this demonstrates (CV bullets)

*Proven by tests (Testcontainers SQL Server) and measured by BenchmarkDotNet:*
- Built a **.NET 10** batch worker that streams a delimited feed with `IAsyncEnumerable` + a bounded
  `System.Threading.Channels` producer/consumer and writes micro-batches with **`SqlBulkCopy`** —
  flat memory regardless of file size.
- Parsed rows with a **zero-allocation `ReadOnlySpan<char>`** parser (no `string.Split` on the hot
  path), routing malformed rows to a **dead-letter** table by reason code; verified a 1,000-row feed
  imports + dead-letters to exactly 1,000.
- Made the bulk write **idempotent and restartable** (`SqlBulkCopy` → staging → `INSERT … WHERE NOT
  EXISTS`) and the whole ingest **file-level idempotent** (`sha256` ledger) — measured **~12× faster**
  than EF `AddRange` + `SaveChanges` (2,105 ms → 171 ms for 20k rows).

## Run it

```bash
# one-shot ingest of a file (creates/migrates the DB on first run)
dotnet run --project src/Reconciliation.Worker -- --file feed.csv

# generate a large sample first (deterministic):
#   see Reconciliation.Core.SampleDataGenerator (used by the tests + benchmark)
```

Scheduled mode (no `--file`) runs a **Quartz** nightly job over the configured landing file.

## Standards / concepts

`IAsyncEnumerable`, `System.Threading.Channels`, `ReadOnlySpan<char>`/`SearchValues<char>`,
`SqlBulkCopy`, EF Core migrations (never `EnsureCreated`), `AsNoTracking`, Central Package Management,
strict (warnings-as-errors) build, Testcontainers not in-memory.
