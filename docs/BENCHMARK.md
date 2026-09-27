# Benchmark — the write path is the bottleneck

The point of this repo is a number a tech lead believes. Ingesting millions of rows lives or dies on
how you get them into the database. The naive answer — map every row to an entity and call
`SaveChanges()` — is the slow one; `SqlBulkCopy` is the fast one.

## Method
- `Ingestion.Benchmarks` (BenchmarkDotNet, `[MemoryDiagnoser]`), each iteration inserts the same
  20,000-row batch into a real SQL Server, truncating the table between iterations.
- Two strategies: EF Core `AddRange` + `SaveChanges` (with change-tracking detection disabled, to be
  fair to EF) vs `BulkTransactionWriter` (`SqlBulkCopy` into a staging table + one set-based
  `INSERT … WHERE NOT EXISTS`).
- Run: `BENCH_SQL="Server=…;Database=bench;…" dotnet run -c Release --project benchmarks/Ingestion.Benchmarks`

## Results (measured)

SQL Server 2022 in Docker, .NET 10, 20,000 rows per insert, BenchmarkDotNet short job:

| Strategy | Mean | Ratio |
|----------|------|-------|
| EF Core `AddRange` + `SaveChanges` | **2,105 ms** | 1.00× (baseline) |
| `SqlBulkCopy` (staging + set insert) | **171 ms** | **~0.08× — about 12× faster** |

## The one-line story for the interview
> "EF `SaveChanges` round-trips per row — at 20k rows that's ~2.1 s; at 2M it is hopeless.
> `SqlBulkCopy` streams the batch in one TDS bulk operation (here ~171 ms, ~12× faster), and I
> stage-then-`INSERT … WHERE NOT EXISTS` so the bulk path stays idempotent and restartable. Combined
> with the `Channel`-bounded streaming reader, the whole ingest runs in flat memory regardless of
> file size."

*Larger batches widen the gap further; the row-by-row cost of `SaveChanges` grows with round-trips,
while `SqlBulkCopy` scales with raw throughput.*
