using Reconciliation.Core;
using Shouldly;

namespace Reconciliation.Tests;

/// <summary>The parser is the validation core; each rejection must carry the right reason code.
/// Pure unit tests — no DB, no Docker.</summary>
public class SpanCsvParserTests
{
    [Fact]
    public void Parses_a_well_formed_row()
    {
        bool ok = SpanCsvParser.TryParse("TX1,ACC7,10.50,eur,2026-02-01,CP42", out var r, out _);

        ok.ShouldBeTrue();
        r.TxnId.ShouldBe("TX1");
        r.Account.ShouldBe("ACC7");
        r.Amount.ShouldBe(10.50m);
        r.Currency.ShouldBe("EUR"); // normalised
        r.ValueDate.ShouldBe(new DateOnly(2026, 2, 1));
        r.Counterparty.ShouldBe("CP42");
    }

    [Theory]
    [InlineData("TX1,ACC7,10.50,EUR,2026-02-01", "COLUMN_COUNT")]           // too few
    [InlineData("TX1,ACC7,10.50,EUR,2026-02-01,CP,EXTRA", "COLUMN_COUNT")]  // too many
    [InlineData(" ,ACC7,10.50,EUR,2026-02-01,CP42", "MISSING_TXN_ID")]
    [InlineData("TX1, ,10.50,EUR,2026-02-01,CP42", "MISSING_ACCOUNT")]
    [InlineData("TX1,ACC7,ten,EUR,2026-02-01,CP42", "BAD_AMOUNT")]
    [InlineData("TX1,ACC7,-5.00,EUR,2026-02-01,CP42", "NON_POSITIVE_AMOUNT")]
    [InlineData("TX1,ACC7,10.50,XXX,2026-02-01,CP42", "BAD_CURRENCY")]
    [InlineData("TX1,ACC7,10.50,EUR,31-02-2026,CP42", "BAD_DATE")]
    [InlineData("TX1,ACC7,10.50,EUR,2026-02-01, ", "MISSING_COUNTERPARTY")]
    public void Rejects_with_the_specific_reason(string line, string expectedReason)
    {
        bool ok = SpanCsvParser.TryParse(line, out _, out var reason);

        ok.ShouldBeFalse();
        reason.ShouldBe(expectedReason);
    }
}
