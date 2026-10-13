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
            throw new GraphQLException(errors.Select(e => e.Message).ToList())
            {
                ErrorType = errors[0].ErrorType ?? StringAt(errors[0].Extensions, "errorType"),
                ErrorField = StringAt(errors[0].ErrorInfo, "field")
                    ?? StringAt(PropertyAt(errors[0].Extensions, "errorInfo"), "field"),
            };

        return payload.Data ?? throw new GraphQLException(["GraphQL response contained no data."]);
    }

    private sealed record GraphQLResponse<T>(
        [property: JsonPropertyName("data")] T? Data,
        [property: JsonPropertyName("errors")] List<GraphQLError>? Errors);

    // AppSync puts errorType/errorInfo on the error itself; the local graphql-yoga backend puts them
    // under extensions. Both are read so pages branch on the type the same way in either.
    private sealed record GraphQLError(
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("errorType")] string? ErrorType,
        [property: JsonPropertyName("errorInfo")] JsonElement? ErrorInfo,
        [property: JsonPropertyName("extensions")] JsonElement? Extensions);

    private static JsonElement? PropertyAt(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(property, out var value) ? value : null;

    private static string? StringAt(JsonElement? element, string property) =>
        PropertyAt(element, property) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}

public sealed class GraphQLException(IReadOnlyList<string> messages)
    : Exception(string.Join("; ", messages))
{
    public IReadOnlyList<string> Messages { get; } = messages;

    // The first error's typed errorType (BadRequest, WarehouseCodeTaken, NotFound...), if any.
    public string? ErrorType { get; init; }

    // The input field the first error points at (errorInfo.field, e.g. "address.zipCode"), if any.
    public string? ErrorField { get; init; }
}
