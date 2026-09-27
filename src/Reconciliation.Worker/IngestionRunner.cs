using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Reconciliation.Core;
using Reconciliation.Data;

namespace Reconciliation.Worker;

public readonly record struct IngestionCounts(long Read, long Imported, long Skipped);

/// <summary>
/// The streaming ingest pipeline. A single <b>producer</b> streams the file line by line
/// (<see cref="FileStreamer"/>), parses each with the zero-allocation <see cref="SpanCsvParser"/>,
/// and pushes valid rows into a <b>bounded <see cref="Channel{T}"/></b>; malformed rows are batched
/// to the dead-letter table. A single <b>consumer</b> drains the channel in micro-batches and writes
/// them with <see cref="BulkTransactionWriter"/>. The bounded channel is the backpressure valve: if
/// the DB writer slows down, the reader blocks instead of buffering the whole file into memory — so
/// the heap stays flat at any file size.
/// </summary>
public sealed class IngestionRunner(
    BulkTransactionWriter writer,
    IDbContextFactory<ReconciliationDbContext> dbFactory,
    IOptions<ReconciliationOptions> options)
{
    private readonly ReconciliationOptions _options = options.Value;

    public async Task<IngestionCounts> RunAsync(string filePath, string sourceFile, CancellationToken ct)
    {
        var channel = Channel.CreateBounded<SettlementRecord>(new BoundedChannelOptions(_options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        long read = 0;
        long skipped = 0;

        var producer = Task.Run(async () =>
        {
            var rejected = new List<RejectedRowEntity>(_options.BatchSize);
            try
            {
                await foreach (var raw in FileStreamer.ReadLinesAsync(filePath, _options.HasHeader, ct))
                {
                    read++;
                    if (SpanCsvParser.TryParse(raw.Text, out var record, out var reason))
                    {
                        await channel.Writer.WriteAsync(record, ct);
                    }
                    else
                    {
                        skipped++;
                        rejected.Add(new RejectedRowEntity
                        {
                            SourceFile = sourceFile,
                            LineNumber = raw.LineNumber,
                            Reason = reason,
                            RawText = raw.Text,
                            RejectedAt = DateTimeOffset.UtcNow
                        });
                        if (rejected.Count >= _options.BatchSize)
                        {
                            await FlushRejectedAsync(rejected, ct);
                        }
                    }
                }
                if (rejected.Count > 0)
                {
                    await FlushRejectedAsync(rejected, ct);
                }
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, ct);

        long imported = 0;
        var batch = new List<SettlementRecord>(_options.BatchSize);
        await foreach (var record in channel.Reader.ReadAllAsync(ct))
        {
            batch.Add(record);
            if (batch.Count >= _options.BatchSize)
            {
                imported += await writer.WriteBatchAsync(batch, sourceFile, ct);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            imported += await writer.WriteBatchAsync(batch, sourceFile, ct);
        }

        await producer; // surface any producer exception and publish read/skipped
        return new IngestionCounts(read, imported, skipped);
    }

    private async Task FlushRejectedAsync(List<RejectedRowEntity> rejected, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.RejectedRows.AddRange(rejected);
        await db.SaveChangesAsync(ct);
        rejected.Clear();
    }
}
