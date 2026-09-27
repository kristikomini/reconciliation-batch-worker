using Microsoft.EntityFrameworkCore;
using Quartz;
using Reconciliation.Data;
using Reconciliation.Worker;

var builder = Host.CreateApplicationBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("Sql")
    ?? "Server=localhost;Database=reconciliation;Trusted_Connection=True;TrustServerCertificate=True";

builder.Services.Configure<ReconciliationOptions>(builder.Configuration.GetSection(ReconciliationOptions.Section));
builder.Services.AddDbContextFactory<ReconciliationDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddSingleton(new BulkTransactionWriter(connectionString));
builder.Services.AddSingleton<IngestionRunner>();
builder.Services.AddSingleton<FileIngestService>();

// One-shot mode: `--file <path>` ingests once and exits. Otherwise the worker runs the Quartz schedule.
string? oneShotFile = GetOption(args, "--file");

if (oneShotFile is null)
{
    var options = builder.Configuration.GetSection(ReconciliationOptions.Section).Get<ReconciliationOptions>()
                  ?? new ReconciliationOptions();
    builder.Services.AddQuartz(q =>
    {
        var key = new JobKey("reconciliation");
        q.AddJob<ReconciliationJob>(key);
        q.AddTrigger(t => t.ForJob(key).WithCronSchedule(options.Cron));
    });
    builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
}

var host = builder.Build();

// The schema is owned by EF Core Migrations, applied here — never EnsureCreated in non-test code.
await using (var db = await host.Services.GetRequiredService<IDbContextFactory<ReconciliationDbContext>>()
                 .CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
}

if (oneShotFile is not null)
{
    var ingest = host.Services.GetRequiredService<FileIngestService>();
    var result = await ingest.IngestAsync(oneShotFile, CancellationToken.None);
    Console.WriteLine($"Ingest {result.Outcome}: read={result.Counts.Read} " +
                      $"imported={result.Counts.Imported} skipped={result.Counts.Skipped}");
    return;
}

await host.RunAsync();

static string? GetOption(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    if (i >= 0 && i + 1 < args.Length)
    {
        return args[i + 1];
    }
    return null;
}
