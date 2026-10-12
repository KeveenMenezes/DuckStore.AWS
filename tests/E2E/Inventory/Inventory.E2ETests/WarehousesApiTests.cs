namespace Inventory.E2ETests;

// The warehouse resolvers' contract (ADR-0052 §4), called on the deployed AppSync with a StockKeeper's
// own access token (SPEC-inventory-warehouses criteria 3, 4, 5, 7).
public sealed class StockKeeperApiTests(StockKeeperSession session) : IClassFixture<StockKeeperSession>
{
    private readonly WarehouseApi _api = new(session);

    [ManagementE2EFact]
    public async Task Create_NormalizesTheInput_AndATakenCodeIsRefused()
    {
        var code = WarehouseApi.NewCode();
        var created = await _api.CreateAsync(WarehouseApi.Input($"  {code.ToLowerInvariant()} "));
        Assert.False(created.HasErrors, created.ToString());
        var warehouse = created.Data!["createWarehouse"]!;
        try
        {
            Assert.Equal(code, (string?)warehouse["code"]);
            Assert.Equal("01310100", (string?)warehouse["address"]!["zipCode"]);
            Assert.True((bool)warehouse["active"]!);

            // Lowercase is the same code once normalized (criterion 3).
            var again = await _api.CreateAsync(WarehouseApi.Input(code.ToLowerInvariant(), priority: 7));
            Assert.Equal("WarehouseCodeTaken", again.ErrorType);
            Assert.Equal("code", (string?)again.ErrorInfo?["field"]);
        }
        finally
        {
            await _api.SetActiveAsync((string)warehouse["id"]!, false);
        }
    }

    [ManagementE2EFact]
    public async Task InvalidInput_IsABadRequestNamingTheField_AndNothingIsWritten()
    {
        var code = WarehouseApi.NewCode();
        var cases = new (object Input, string Field)[]
        {
            (WarehouseApi.Input(code, zipCode: "0131010"), "address.zipCode"),
            (WarehouseApi.Input(code, priority: 0), "priority"),
            (WarehouseApi.Input("-BAD"), "code"),
        };

        foreach (var (input, field) in cases)
        {
            var response = await _api.CreateAsync(input);
            Assert.Equal("BadRequest", response.ErrorType);
            Assert.Equal(field, (string?)response.ErrorInfo?["field"]);
        }

        var list = await _api.ListAsync();
        Assert.DoesNotContain(list.Data!["warehouses"]!.AsArray(), w => (string?)w!["code"] == code);
    }

    [ManagementE2EFact]
    public async Task Update_ChangesNameAddressAndPriority_ButNeverTheCode()
    {
        var code = WarehouseApi.NewCode();
        var warehouse = await _api.CreateValidAsync(code);
        var id = (string)warehouse["id"]!;
        try
        {
            var updated = await _api.UpdateAsync(new
            {
                id,
                name = "E2E renamed",
                address = new
                {
                    street = "Rua da Assembleia", number = "10", complement = "Galpão 2", district = "Centro",
                    city = "Rio de Janeiro", state = "rj", zipCode = "20011-000",
                },
                priority = 900,
            });
            Assert.False(updated.HasErrors, updated.ToString());
            var after = updated.Data!["updateWarehouse"]!;
            Assert.Equal("E2E renamed", (string?)after["name"]);
            Assert.Equal("RJ", (string?)after["address"]!["state"]);
            Assert.Equal("Galpão 2", (string?)after["address"]!["complement"]);
            Assert.Equal(900, (int)after["priority"]!);
            Assert.Equal(code, (string?)after["code"]);
        }
        finally
        {
            await _api.SetActiveAsync(id, false);
        }
    }

    [ManagementE2EFact]
    public async Task UnknownIds_AndCodeGuards_AreNotWarehouses()
    {
        var code = WarehouseApi.NewCode();
        var warehouse = await _api.CreateValidAsync(code);
        try
        {
            foreach (var id in new[] { Guid.NewGuid().ToString(), $"CODE#{code}" })
            {
                var deactivate = await _api.SetActiveAsync(id, false);
                Assert.Equal("NotFound", deactivate.ErrorType);

                var read = await _api.GetAsync(id);
                Assert.False(read.HasErrors, read.ToString());
                Assert.Null(read.Data!["warehouse"]);
            }

            // The list never returns a guard as a warehouse.
            var list = await _api.ListAsync();
            Assert.Single(list.Data!["warehouses"]!.AsArray(), w => (string?)w!["code"] == code);
        }
        finally
        {
            await _api.SetActiveAsync((string)warehouse["id"]!, false);
        }
    }
}

// Criterion 8: a Seller's token is refused by every warehouse operation, reads included.
public sealed class SellerApiTests(SellerSession session) : IClassFixture<SellerSession>
{
    private readonly WarehouseApi _api = new(session);

    [ManagementE2EFact]
    public async Task EveryOperation_IsUnauthorized()
    {
        var id = Guid.NewGuid().ToString();
        var responses = new[]
        {
            await _api.ListAsync(),
            await _api.GetAsync(id),
            await _api.CreateAsync(WarehouseApi.Input(WarehouseApi.NewCode())),
            await _api.UpdateAsync(new { id, name = "x", address = new { street = "a", number = "1", district = "b", city = "c", state = "SP", zipCode = "01310100" }, priority = 1 }),
            await _api.SetActiveAsync(id, false),
        };

        Assert.All(responses, response => Assert.Equal("Unauthorized", response.ErrorType));
    }
}

// Criterion 8: a storefront customer (shopping pool, group Customer) is refused too, through the SPA's BFF.
public sealed class CustomerApiTests(StorefrontSession session) : IClassFixture<StorefrontSession>
{
    [ManagementE2EFact(requiresStorefront: true)]
    public async Task Warehouses_IsUnauthorizedForACustomer()
    {
        var response = await session.GraphQLAsync("{ warehouses { id code } }");

        Assert.Equal("Unauthorized", response.ErrorType);
    }
}
