using System.Data;
using Microsoft.Data.SqlClient;
using Reconciliation.Core;

namespace Reconciliation.Data;

/// <summary>
/// Writes a batch of settlement rows with <see cref="SqlBulkCopy"/> — the fast path that makes 2M
/// rows tractable. To stay <b>idempotent and restartable</b>, it bulk-loads the batch into a
/// session-scoped staging table and then does a single set-based
/// <c>INSERT … WHERE NOT EXISTS</c> into the target, so a re-run (or an overlapping restart) never
/// hits a duplicate-key on the natural key <c>txn_id</c>.
/// </summary>
public sealed class BulkTransactionWriter(string connectionString)
{
    public async Task<int> WriteBatchAsync(
        IReadOnlyList<SettlementRecord> batch, string sourceFile, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await ExecuteAsync(connection,
            """
            CREATE TABLE #stage (
                txn_id nvarchar(64), account nvarchar(64), amount decimal(18,2),
                currency nchar(3), value_date date, counterparty nvarchar(128));
            """, ct);

        using (var bulk = new SqlBulkCopy(connection) { DestinationTableName = "#stage", BatchSize = batch.Count })
        {
            bulk.ColumnMappings.Add("txn_id", "txn_id");
            bulk.ColumnMappings.Add("account", "account");
            bulk.ColumnMappings.Add("amount", "amount");
            bulk.ColumnMappings.Add("currency", "currency");
            bulk.ColumnMappings.Add("value_date", "value_date");
            bulk.ColumnMappings.Add("counterparty", "counterparty");
            using var table = ToDataTable(batch);
            await bulk.WriteToServerAsync(table, ct);
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO transaction_record
                (txn_id, account, amount, currency, value_date, counterparty, source_file, imported_at)
            SELECT s.txn_id, s.account, s.amount, s.currency, s.value_date, s.counterparty, @src, SYSDATETIMEOFFSET()
            FROM #stage s
            WHERE NOT EXISTS (SELECT 1 FROM transaction_record t WHERE t.txn_id = s.txn_id);
            """;
        insert.Parameters.Add(new SqlParameter("@src", sourceFile));
        return await insert.ExecuteNonQueryAsync(ct);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static DataTable ToDataTable(IReadOnlyList<SettlementRecord> batch)
    {
        var table = new DataTable();
        table.Columns.Add("txn_id", typeof(string));
        table.Columns.Add("account", typeof(string));
        table.Columns.Add("amount", typeof(decimal));
        table.Columns.Add("currency", typeof(string));
        table.Columns.Add("value_date", typeof(DateTime));
        table.Columns.Add("counterparty", typeof(string));

        foreach (var r in batch)
        {
            table.Rows.Add(r.TxnId, r.Account, r.Amount, r.Currency,
                r.ValueDate.ToDateTime(TimeOnly.MinValue), r.Counterparty);
        }
        return table;
    }
}
