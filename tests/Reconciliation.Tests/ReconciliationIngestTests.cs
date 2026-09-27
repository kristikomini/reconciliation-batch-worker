using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reconciliation.Core;
using Reconciliation.Data;
using Reconciliation.Worker;
using Shouldly;
using Testcontainers.MsSql;

namespace Reconciliation.Tests;

/// <summary>
/// End-to-end against a real SQL Server (Testcontainers): generate a feed, run the streaming
/// Channel → SqlBulkCopy pipeline, and assert the reconciliation invariants — every row is either
/// imported or dead-lettered, the counts add up, and a re-drop of the same bytes is a no-op.
/// </summary>
public sealed class ReconciliationIngestTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder().Build();
    private ServiceProvider _services = default!;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<ReconciliationOptions>(o => { o.BatchSize = 500; o.HasHeader = true; });
        services.AddDbContextFactory<ReconciliationDbContext>(o => o.UseSqlServer(_sql.GetConnectionString()));
        services.AddSingleton(new BulkTransactionWriter(_sql.GetConnectionString()));
        services.AddSingleton<IngestionRunner>();
        services.AddSingleton<FileIngestService>();
        _services = services.BuildServiceProvider();

        await using var db = await Factory().CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _sql.DisposeAsync();
    }

    private IDbContextFactory<ReconciliationDbContext> Factory() =>
        _services.GetRequiredService<IDbContextFactory<ReconciliationDbContext>>();

    [IntegrationFact]
    public async Task Imports_good_rows_dead_letters_bad_ones_and_balances()
    {
        string file = Path.GetTempFileName();
        await SampleDataGenerator.WriteAsync(file, rows: 1000, badFraction: 0.05, seed: 7);

        var result = await _services.GetRequiredService<FileIngestService>().IngestAsync(file, default);

        result.Outcome.ShouldBe(FileIngestService.Outcome.Ran);
        result.Counts.Read.ShouldBe(1000);

        await using var db = await Factory().CreateDbContextAsync();
        long imported = await db.Transactions.LongCountAsync();
        long rejected = await db.RejectedRows.LongCountAsync();

        (imported + rejected).ShouldBe(1000);      // nothing lost
        rejected.ShouldBeGreaterThan(0);           // the salted-in bad rows were caught
        result.Counts.Imported.ShouldBe(imported);
        result.Counts.Skipped.ShouldBe(rejected);

        File.Delete(file);
    }

    [IntegrationFact]
    public async Task Re_ingesting_the_same_file_is_a_no_op()
    {
        string file = Path.GetTempFileName();
        await SampleDataGenerator.WriteAsync(file, rows: 500, badFraction: 0.0, seed: 9);
        var ingest = _services.GetRequiredService<FileIngestService>();

        await ingest.IngestAsync(file, default);
        long afterFirst;
        await using (var db = await Factory().CreateDbContextAsync())
        {
            afterFirst = await db.Transactions.LongCountAsync();
        }

        var second = await ingest.IngestAsync(file, default);

        second.Outcome.ShouldBe(FileIngestService.Outcome.SkippedAlreadyIngested);
        await using (var db = await Factory().CreateDbContextAsync())
        {
            (await db.Transactions.LongCountAsync()).ShouldBe(afterFirst); // no duplicates
        }

        File.Delete(file);
    }
}
