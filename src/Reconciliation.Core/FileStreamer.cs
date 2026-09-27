using System.Runtime.CompilerServices;

namespace Reconciliation.Core;

/// <summary>
/// Streams a file one line at a time as an <see cref="IAsyncEnumerable{T}"/>. The whole file is
/// never held in memory, so a 2-million-row feed ingests in constant heap regardless of size — the
/// reason the worker runs comfortably under a small memory cap.
/// </summary>
public static class FileStreamer
{
    public static async IAsyncEnumerable<RawLine> ReadLinesAsync(
        string path, bool hasHeader, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(path);
        long lineNumber = 0;

        if (hasHeader)
        {
            await reader.ReadLineAsync(ct);
            lineNumber++;
        }

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            lineNumber++;
            yield return new RawLine(lineNumber, line);
        }
    }
}
