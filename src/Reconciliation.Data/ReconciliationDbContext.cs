using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Reconciliation.Data;

public class ReconciliationDbContext(DbContextOptions<ReconciliationDbContext> options) : DbContext(options)
{
    public DbSet<TransactionRecordEntity> Transactions => Set<TransactionRecordEntity>();
    public DbSet<ImportFile> ImportFiles => Set<ImportFile>();
    public DbSet<RejectedRowEntity> RejectedRows => Set<RejectedRowEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportFile>().HasIndex(f => f.Sha256).IsUnique();
        modelBuilder.Entity<RejectedRowEntity>().HasIndex(r => new { r.SourceFile, r.Reason });
    }
}

/// <summary>Lets <c>dotnet ef</c> build the context at design time (for `migrations add`) without a
/// running host. Not used at runtime — the worker configures the real connection string.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ReconciliationDbContext>
{
    public ReconciliationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ReconciliationDbContext>()
            .UseSqlServer("Server=localhost;Database=reconciliation;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new ReconciliationDbContext(options);
    }
}
