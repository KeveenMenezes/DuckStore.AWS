namespace AppHost.Observability;

public static class ObservabilityExtensions
{
    public static IResourceBuilder<ElasticsearchResource> AddObservability(this IDistributedApplicationBuilder builder)
    {
        var elasticsearch = builder.AddElasticsearch("elasticsearch")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithEnvironment("xpack.security.enabled", "false")
            .WithEnvironment("discovery.type", "single-node")
            .WithDataVolume();

        _ = builder.AddContainer("kibana", "docker.elastic.co/kibana/kibana", "8.17.3")
            .WithEnvironment("ELASTICSEARCH_HOSTS", "http://elasticsearch:9200")
            .WithHttpEndpoint(port: 5601, targetPort: 5601, name: "http")
            .WaitFor(elasticsearch)
            .WithReference(elasticsearch);

        return elasticsearch;
    }
}
