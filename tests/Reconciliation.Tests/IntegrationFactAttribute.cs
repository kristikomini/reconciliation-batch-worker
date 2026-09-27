namespace Reconciliation.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that runs only where a real SQL Server (via Testcontainers +
/// Docker) is available — CI, or locally with <c>RUN_DB_TESTS=1</c>. Elsewhere it is skipped
/// cleanly, so the unit suite stays fast and Docker-free.
/// </summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        bool enabled = Environment.GetEnvironmentVariable("CI") is not null
                       || Environment.GetEnvironmentVariable("RUN_DB_TESTS") == "1";
        if (!enabled)
        {
            Skip = "DB integration test — set RUN_DB_TESTS=1 (needs Docker) or run in CI.";
        }
    }
}
