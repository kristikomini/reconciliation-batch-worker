using System.Buffers;
using System.Globalization;

namespace Reconciliation.Core;

/// <summary>
/// Parses one CSV line into a <see cref="SettlementRecord"/> directly over a
/// <see cref="ReadOnlySpan{Char}"/> — no <c>string.Split</c>, so no intermediate <c>string[]</c> or
/// per-field string garbage on the hot path. Strings are materialised only for the columns of a row
/// that passes validation. Every rejection returns a specific reason code, so the dead-letter report
/// can break rejections down by cause.
///
/// <para>Layout: <c>txn_id,account,amount,currency,value_date,counterparty</c>.</para>
/// </summary>
public static class SpanCsvParser
{
    private static readonly SearchValues<char> Comma = SearchValues.Create(",");

    private static bool IsAllowedCurrency(ReadOnlySpan<char> c) =>
        c.Equals("EUR", StringComparison.OrdinalIgnoreCase) ||
        c.Equals("USD", StringComparison.OrdinalIgnoreCase) ||
        c.Equals("GBP", StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(ReadOnlySpan<char> line, out SettlementRecord record, out string reason)
    {
        record = default;

        // Split into exactly six fields by walking the five commas — spans into the line, no copies.
        if (!NextField(ref line, out var txnIdSpan) ||
            !NextField(ref line, out var accountSpan) ||
            !NextField(ref line, out var amountSpan) ||
            !NextField(ref line, out var currencySpan) ||
            !NextField(ref line, out var dateSpan))
        {
            reason = "COLUMN_COUNT";
            return false;
        }
        // The sixth field is the remainder; a comma still present means too many columns.
        if (line.IndexOfAny(Comma) >= 0)
        {
            reason = "COLUMN_COUNT";
            return false;
        }
        ReadOnlySpan<char> counterpartySpan = line.Trim();

        ReadOnlySpan<char> txnId = txnIdSpan.Trim();
        if (txnId.IsEmpty) { reason = "MISSING_TXN_ID"; return false; }

        ReadOnlySpan<char> account = accountSpan.Trim();
        if (account.IsEmpty) { reason = "MISSING_ACCOUNT"; return false; }

        if (!decimal.TryParse(amountSpan.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount))
        {
            reason = "BAD_AMOUNT"; return false;
        }
        if (amount <= 0) { reason = "NON_POSITIVE_AMOUNT"; return false; }

        ReadOnlySpan<char> currency = currencySpan.Trim();
        if (!IsAllowedCurrency(currency)) { reason = "BAD_CURRENCY"; return false; }

        if (!DateOnly.TryParse(dateSpan.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly valueDate))
        {
            reason = "BAD_DATE"; return false;
        }

        if (counterpartySpan.IsEmpty) { reason = "MISSING_COUNTERPARTY"; return false; }

        record = new SettlementRecord(
            txnId.ToString(), account.ToString(), amount,
            currency.ToString().ToUpperInvariant(), valueDate, counterpartySpan.ToString());
        reason = "";
        return true;
    }

    /// <summary>Slices off the next comma-delimited field, advancing <paramref name="rest"/> past it.
    /// Returns false when there is no further comma (the caller treats the leftover as the last field).</summary>
    private static bool NextField(ref ReadOnlySpan<char> rest, out ReadOnlySpan<char> field)
    {
        int comma = rest.IndexOfAny(Comma);
        if (comma < 0)
        {
            field = default;
            return false;
        }
        field = rest[..comma];
        rest = rest[(comma + 1)..];
        return true;
    }
}
