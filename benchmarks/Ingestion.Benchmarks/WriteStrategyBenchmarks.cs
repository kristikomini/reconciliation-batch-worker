using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Reconciliation.Data;

namespace Ingestion.Benchmarks;

/// <summary>
/// The write-path shootout: inserting the same batch of settlement rows with EF Core
/// <c>AddRange</c>+<c>SaveChanges</c> vs <see cref="BulkTransactionWriter"/> (SqlBulkCopy staging +
/// set-based insert). Requires a SQL Server — connection string from the <c>BENCH_SQL</c> env var.
///
/// <para>Run: <c>BENCH_SQL="Server=...;Database=bench;..." dotnet run -c Release --project benchmarks/Ingestion.Benchmarks</c></para>
/// </summary>
[MemoryDiagnoser]
public class WriteStrategyBenchmarks
{
    [Params(20_000)] public int Rows { get; set; }

    private string _cs = default!;
    private List<Reconciliation.Core.SettlementRecord> _records = default!;
    private BulkTransactionWriter _bulk = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _cs = Environment.GetEnvironmentVariable("BENCH_SQL")
              ?? "Server=localhost,11433;Database=bench;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True";

        using (var db = NewContext())
        {
            db.Database.Migrate();
        }
        _bulk = new BulkTransactionWriter(_cs);

        var baseDate = new DateOnly(2026, 1, 1);
        _records = Enumerable.Range(0, Rows).Select(i => new Reconciliation.Core.SettlementRecord(
            $"BX{i}", $"ACC{i % 1000}", 10.00m + (i % 100), "EUR", baseDate.AddDays(i % 365), $"CP{i % 500}")).ToList();
    }

    [IterationSetup]
    public void ClearTable()
    {
        using var db = NewContext();
        db.Database.ExecuteSqlRaw("TRUNCATE TABLE transaction_record;");
    }

    [Benchmark(Baseline = true, Description = "EF AddRange + SaveChanges")]
    public async Task EfAddRange()
    {
        await using var db = NewContext();
        db.ChangeTracker.AutoDetectChangesEnabled = false; // fairer: don't pay change-detection per row
        foreach (var r in _records)
        {
            db.Transactions.Add(new TransactionRecordEntity
            {
                TxnId = r.TxnId, Account = r.Account, Amount = r.Amount, Currency = r.Currency,
                ValueDate = r.ValueDate, Counterparty = r.Counterparty, SourceFile = "bench",
                ImportedAt = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    [Benchmark(Description = "SqlBulkCopy (staging + set insert)")]
    public async Task SqlBulkCopyWriter()
    {
        await _bulk.WriteBatchAsync(_records, "bench", CancellationToken.None);
    }

    private ReconciliationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ReconciliationDbContext>().UseSqlServer(_cs).Options);
}
