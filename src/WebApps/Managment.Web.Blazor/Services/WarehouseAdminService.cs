namespace Managment.Web.Blazor.Services;

/// <summary>
/// Typed wrapper over the warehouse GraphQL operations (ADR-0052). Operation shapes mirror
/// graphql/schema.graphql. Every operation is StockKeeper/Admin only — enforced server-side by the
/// resolvers; failures surface as <see cref="GraphQLException"/> with ErrorType (BadRequest,
/// WarehouseCodeTaken, NotFound, Unauthorized) and, for input errors, ErrorField.
/// </summary>
public sealed class WarehouseAdminService(GraphQLClient gql)
{
    private const string WarehouseFields =
        """
        id
        code
        name
        address { street number complement district city state zipCode }
        priority
        active
        createdAt
        updatedAt
        """;

    private const string GetWarehousesQuery =
        $$"""
        query GetWarehouses {
          warehouses { {{WarehouseFields}} }
        }
        """;

    private const string GetWarehouseQuery =
        $$"""
        query GetWarehouse($id: ID!) {
          warehouse(id: $id) { {{WarehouseFields}} }
        }
        """;

    private const string CreateWarehouseMutation =
        $$"""
        mutation CreateWarehouse($input: CreateWarehouseInput!) {
          createWarehouse(input: $input) { {{WarehouseFields}} }
        }
        """;

    private const string UpdateWarehouseMutation =
        $$"""
        mutation UpdateWarehouse($input: UpdateWarehouseInput!) {
          updateWarehouse(input: $input) { {{WarehouseFields}} }
        }
        """;

    private const string SetWarehouseActiveMutation =
        $$"""
        mutation SetWarehouseActive($id: ID!, $active: Boolean!) {
          setWarehouseActive(id: $id, active: $active) { {{WarehouseFields}} }
        }
        """;

    // Sorted by priority, then code, by the resolver.
    public async Task<List<Warehouse>> GetWarehousesAsync()
    {
        var data = await gql.SendAsync<WarehousesData>(GetWarehousesQuery);
        return data.Warehouses;
    }

    public async Task<Warehouse?> GetWarehouseAsync(string id)
    {
        var data = await gql.SendAsync<WarehouseData>(GetWarehouseQuery, new { id });
        return data.Warehouse;
    }

    public async Task<Warehouse> CreateWarehouseAsync(WarehouseFormModel model)
    {
        var input = new
        {
            code = model.Code.Trim(),
            name = model.Name.Trim(),
            address = AddressOf(model),
            priority = model.Priority,
        };
        var data = await gql.SendAsync<CreateWarehouseData>(CreateWarehouseMutation, new { input });
        return data.CreateWarehouse;
    }

    public async Task<Warehouse> UpdateWarehouseAsync(string id, WarehouseFormModel model)
    {
        var input = new
        {
            id,
            name = model.Name.Trim(),
            address = AddressOf(model),
            priority = model.Priority,
        };
        var data = await gql.SendAsync<UpdateWarehouseData>(UpdateWarehouseMutation, new { input });
        return data.UpdateWarehouse;
    }

    public async Task<Warehouse> SetActiveAsync(string id, bool active)
    {
        var data = await gql.SendAsync<SetWarehouseActiveData>(SetWarehouseActiveMutation, new { id, active });
        return data.SetWarehouseActive;
    }

    private static object AddressOf(WarehouseFormModel model) => new
    {
        street = model.Street.Trim(),
        number = model.Number.Trim(),
        complement = string.IsNullOrWhiteSpace(model.Complement) ? null : model.Complement.Trim(),
        district = model.District.Trim(),
        city = model.City.Trim(),
        state = model.State,
        zipCode = model.ZipCode.Trim(),
    };

    private sealed record WarehousesData(List<Warehouse> Warehouses);

    private sealed record WarehouseData(Warehouse? Warehouse);

    private sealed record CreateWarehouseData(Warehouse CreateWarehouse);

    private sealed record UpdateWarehouseData(Warehouse UpdateWarehouse);

    private sealed record SetWarehouseActiveData(Warehouse SetWarehouseActive);
}
