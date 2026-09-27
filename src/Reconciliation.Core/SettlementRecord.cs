namespace Reconciliation.Core;

/// <summary>
/// A validated settlement row. A <c>readonly record struct</c> so a batch of these lives on the
/// stack / in a pooled array without per-row heap allocation — at millions of rows that is the
/// difference between a flat and a thrashing GC.
/// </summary>
public readonly record struct SettlementRecord(
    string TxnId,
    string Account,
    decimal Amount,
    string Currency,
    DateOnly ValueDate,
    string Counterparty);

/// <summary>A physical line paired with its 1-based number, so a rejected row can be dead-lettered
/// with the exact location in the source file.</summary>
public readonly record struct RawLine(long LineNumber, string Text);

/// <summary>A rejected row: the line number, a machine-usable reason code, and the raw text.</summary>
public readonly record struct RejectedRow(long LineNumber, string Reason, string RawText);
