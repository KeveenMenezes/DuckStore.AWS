using System.Text.Json.Nodes;

namespace Inventory.E2ETests;

// The warehouse GraphQL operations (ADR-0052), sent straight to AppSync as a signed-in staff member.
public sealed class WarehouseApi(ManagementSession session)
{
    private const string Fields =
        "id code name priority active address { street number complement district city state zipCode }";

    // Codes are 2-20 chars of [A-Z0-9-] (ADR-0052 §2): "E2E-" plus 8 random uppercase letters/digits.
    // Warehouses are never deleted, so every test warehouse gets a fresh code and is deactivated after.
    public static string NewCode() => $"E2E-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    public static object Input(string code, int priority = 500, string zipCode = "01310-100") => new
    {
        code,
        name = $"E2E warehouse {code}",
        address = new
        {
            street = "Avenida Paulista",
            number = "1000",
            complement = (string?)null,
            district = "Bela Vista",
            city = "São Paulo",
            state = "SP",
            zipCode,
        },
        priority,
    };

    public Task<GraphQLResponse> CreateAsync(object input) => session.GraphQLAsync(
        $"mutation($input: CreateWarehouseInput!) {{ createWarehouse(input: $input) {{ {Fields} }} }}", new { input });

    public Task<GraphQLResponse> UpdateAsync(object input) => session.GraphQLAsync(
        $"mutation($input: UpdateWarehouseInput!) {{ updateWarehouse(input: $input) {{ {Fields} }} }}", new { input });

    public Task<GraphQLResponse> SetActiveAsync(string id, bool active) => session.GraphQLAsync(
        $"mutation($id: ID!, $active: Boolean!) {{ setWarehouseActive(id: $id, active: $active) {{ {Fields} }} }}",
        new { id, active });

    public Task<GraphQLResponse> ListAsync() => session.GraphQLAsync($"{{ warehouses {{ {Fields} }} }}");

    public Task<GraphQLResponse> GetAsync(string id) => session.GraphQLAsync(
        $"query($id: ID!) {{ warehouse(id: $id) {{ {Fields} }} }}", new { id });

    // Creates a warehouse that must succeed, returning it.
    public async Task<JsonNode> CreateValidAsync(string code, int priority = 500)
    {
        var response = await CreateAsync(Input(code, priority));
        Assert.False(response.HasErrors, response.ToString());
        return response.Data!["createWarehouse"]!;
    }
}
