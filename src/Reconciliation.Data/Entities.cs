using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Reconciliation.Data;

/// <summary>An imported settlement row. <see cref="TxnId"/> is the natural key (unique), which is
/// what makes re-ingesting the same file a no-op instead of a duplicate.</summary>
[Table("transaction_record")]
public class TransactionRecordEntity
{
    [Key, Column("txn_id"), MaxLength(64)] public string TxnId { get; set; } = "";
    [Column("account"), MaxLength(64)] public string Account { get; set; } = "";
    [Column("amount", TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Column("currency"), MaxLength(3)] public string Currency { get; set; } = "";
    [Column("value_date")] public DateOnly ValueDate { get; set; }
    [Column("counterparty"), MaxLength(128)] public string Counterparty { get; set; } = "";
    [Column("source_file"), MaxLength(256)] public string SourceFile { get; set; } = "";
    [Column("imported_at")] public DateTimeOffset ImportedAt { get; set; }
}

/// <summary>The ledger of files seen: file-level idempotency plus the per-run reconciliation counts.</summary>
[Table("import_file")]
public class ImportFile
{
    public long Id { get; set; }
    [MaxLength(256)] public string FileName { get; set; } = "";
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    [MaxLength(16)] public string Status { get; set; } = "";
    public long RowsRead { get; set; }
    public long RowsImported { get; set; }
    public long RowsSkipped { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>Dead-letter: every skipped row with its reason, so nothing is lost silently.</summary>
[Table("rejected_row")]
public class RejectedRowEntity
{
    public long Id { get; set; }
    [MaxLength(256)] public string SourceFile { get; set; } = "";
    public long LineNumber { get; set; }
    [MaxLength(32)] public string Reason { get; set; } = "";
    public string RawText { get; set; } = "";
    public DateTimeOffset RejectedAt { get; set; }
}
