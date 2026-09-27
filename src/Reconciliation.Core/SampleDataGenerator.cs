using System.Globalization;

namespace Reconciliation.Core;

/// <summary>
/// Generates a deterministic sample feed (same seed → identical bytes) so benchmark runs are
/// comparable and tests are repeatable. A configurable fraction of rows is deliberately malformed
/// to exercise the dead-letter path, not just the happy path.
/// </summary>
public static class SampleDataGenerator
{
    private static readonly string[] Currencies = ["EUR", "USD", "GBP"];

    public static async Task<long> WriteAsync(string path, long rows, double badFraction = 0.0,
                                              int seed = 42, CancellationToken ct = default)
    {
        var rnd = new Random(seed);
        var baseDate = new DateOnly(2026, 1, 1);

        await using var writer = new StreamWriter(path);
        await writer.WriteLineAsync("txn_id,account,amount,currency,value_date,counterparty");

        for (long i = 0; i < rows; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (rnd.NextDouble() < badFraction)
            {
                await writer.WriteLineAsync(Malformed(i, rnd));
                continue;
            }
            string currency = Currencies[rnd.Next(Currencies.Length)];
            decimal amount = (1 + rnd.Next(1_000_000)) / 100m;
            DateOnly date = baseDate.AddDays(rnd.Next(365));
            await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"TX{i},ACC{rnd.Next(100_000)},{amount},{currency},{date:yyyy-MM-dd},CP{rnd.Next(5_000)}"));
        }
        return rows;
    }

    private static string Malformed(long i, Random rnd) => rnd.Next(4) switch
    {
        0 => $"TX{i},ACC1,-5.00,EUR,2026-02-01,CP1",     // non-positive amount
        1 => $"TX{i},ACC1,10.00,XXX,2026-02-01,CP1",     // bad currency
        2 => $"TX{i},ACC1,10.00,EUR,not-a-date,CP1",     // bad date
        _ => $"TX{i},ACC1,10.00,EUR,2026-02-01"          // missing column
    };
}
