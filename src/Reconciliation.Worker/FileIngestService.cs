using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Reconciliation.Data;

namespace Reconciliation.Worker;

/// <summary>
/// Decides whether — and how — to ingest a file, and owns <b>file-level idempotency</b>: a
/// byte-identical re-drop that already completed is a no-op; a new or changed file runs; a
/// previously-failed file re-runs (and the bulk writer's WHERE NOT EXISTS makes the retry safe).
/// The per-run reconciliation counts are stamped onto the <c>import_file</c> ledger.
/// </summary>
public sealed class FileIngestService(
    IngestionRunner runner,
    IDbContextFactory<ReconciliationDbContext> dbFactory,
    ILogger<FileIngestService> logger)
{
    public enum Outcome { Ran, SkippedAlreadyIngested }

    public readonly record struct Result(Outcome Outcome, string Sha256, IngestionCounts Counts);

    public async Task<Result> IngestAsync(string filePath, CancellationToken ct)
    {
        string sha = await Sha256Async(filePath, ct);
        string fileName = Path.GetFileName(filePath);

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var existing = await db.ImportFiles.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Sha256 == sha, ct);
            if (existing is { Status: "COMPLETED" })
            {
                logger.LogInformation("File {File} (sha {Sha}) already ingested — skipping.", fileName, sha[..12]);
                return new Result(Outcome.SkippedAlreadyIngested, sha, default);
            }
            if (existing is null)
            {
                db.ImportFiles.Add(new ImportFile
                {
                    FileName = fileName, Sha256 = sha, Status = "STARTED", StartedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(ct);
            }
        }

        IngestionCounts counts = await runner.RunAsync(filePath, fileName, ct);

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var row = await db.ImportFiles.FirstAsync(f => f.Sha256 == sha, ct);
            row.Status = "COMPLETED";
            row.RowsRead = counts.Read;
            row.RowsImported = counts.Imported;
            row.RowsSkipped = counts.Skipped;
            row.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("Reconciliation report for {File}: read={Read} imported={Imported} skipped={Skipped}",
            fileName, counts.Read, counts.Imported, counts.Skipped);
        return new Result(Outcome.Ran, sha, counts);
    }

    private static async Task<string> Sha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }
}
