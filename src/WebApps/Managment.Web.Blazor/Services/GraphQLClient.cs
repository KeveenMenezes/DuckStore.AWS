using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Managment.Web.Blazor.Services;

/// <summary>
/// Minimal GraphQL-over-HTTP client: POSTs { query, variables } to the configured
/// endpoint and unwraps data/errors. Hand-rolled on purpose — a handful of operations
/// doesn't justify a codegen client library in the WASM payload.
/// </summary>
public sealed class GraphQLClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> SendAsync<T>(string query, object? variables = null, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("", new { query, variables }, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GraphQLResponse<T>>(JsonOptions, ct)
            ?? throw new GraphQLException(["Empty GraphQL response."]);

        if (payload.Errors is { Count: > 0 } errors)
            throw new GraphQLException(errors.Select(e => e.Message).ToList());

        return payload.Data ?? throw new GraphQLException(["GraphQL response contained no data."]);
    }

    private sealed record GraphQLResponse<T>(
        [property: JsonPropertyName("data")] T? Data,
        [property: JsonPropertyName("errors")] List<GraphQLError>? Errors);

    private sealed record GraphQLError([property: JsonPropertyName("message")] string Message);
}

public sealed class GraphQLException(IReadOnlyList<string> messages)
    : Exception(string.Join("; ", messages))
{
    public IReadOnlyList<string> Messages { get; } = messages;
}
