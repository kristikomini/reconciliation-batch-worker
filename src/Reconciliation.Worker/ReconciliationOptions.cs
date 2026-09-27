namespace Reconciliation.Worker;

/// <summary>Tuning knobs for the ingest, all overridable per environment.</summary>
public sealed class ReconciliationOptions
{
    public const string Section = "Reconciliation";

    /// <summary>Rows per bulk-copy micro-batch (commit granularity).</summary>
    public int BatchSize { get; set; } = 5_000;

    /// <summary>Bounded channel capacity — the backpressure buffer between the reader and the writer.</summary>
    public int ChannelCapacity { get; set; } = 20_000;

    public bool HasHeader { get; set; } = true;

    /// <summary>Cron for the scheduled nightly run (Quartz). Default: 02:00 every day.</summary>
    public string Cron { get; set; } = "0 0 2 * * ?";

    /// <summary>The file the scheduled job ingests, when running on a schedule rather than --file.</summary>
    public string? LandingFile { get; set; }
}
