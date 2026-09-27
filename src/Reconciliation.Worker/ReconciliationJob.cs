using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Reconciliation.Worker;

/// <summary>The scheduled nightly reconciliation: Quartz fires this on the configured cron, and it
/// ingests the landing file through the idempotent <see cref="FileIngestService"/>.</summary>
[DisallowConcurrentExecution]
public sealed class ReconciliationJob(
    FileIngestService ingestService,
    IOptions<ReconciliationOptions> options,
    ILogger<ReconciliationJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        string? file = options.Value.LandingFile;
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            logger.LogWarning("Scheduled run skipped: no landing file at '{File}'.", file);
            return;
        }
        await ingestService.IngestAsync(file, context.CancellationToken);
    }
}
