using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.ServiceDefaults;

/// <summary>
/// Centralized Elasticsearch registration aligned with the DuckStore Elasticsearch Integration Guideline (ADR-0001).
/// <para>
/// Each API must use a single <c>ElasticsearchClient</c> instance registered via this extension.
/// Separation of concerns (search, logs, traces, analytics) happens at the INDEX or DATA STREAM level,
/// not at the client level.
/// </para>
/// <para>
/// See: docs/adr/0001-elasticsearch-integration-guideline.md
/// </para>
/// </summary>
public static class ElasticsearchExtensions
{
    private const string DefaultConnectionName = "elasticsearch";

    /// <summary>
    /// Registers a single <see cref="Elastic.Clients.Elasticsearch.ElasticsearchClient"/> using the
    /// standard <c>"elasticsearch"</c> connection name from Aspire configuration.
    /// <para>
    /// This client is shared for all Elasticsearch operations: search indexing, analytics events,
    /// and any direct queries. Observability (APM traces and logs) flows automatically
    /// via OpenTelemetry and does not require manual writes through this client.
    /// </para>
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHostApplicationBuilder AddElasticsearch(this IHostApplicationBuilder builder)
    {
        builder.AddElasticsearchClient(connectionName: DefaultConnectionName);
        return builder;
    }
}
