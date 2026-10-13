using Managment.Web.Blazor.Services;
using Microsoft.AspNetCore.Components;

namespace Managment.Web.Blazor.Pages.Stock;

/// <summary>
/// What the receipt, adjustment and transfer forms share: the submit flow with its idempotency-key
/// renewal rule (ADR-0052 §7), the automatic retry of a TransactionConflict, and the mapping of a
/// refusal to the field it names (errorInfo.field) or to the form's banner.
/// </summary>
public abstract class StockMovementFormBase<TModel> : ComponentBase
    where TModel : StockMovementFormModel, new()
{
    // The refusals the resolvers raise on purpose; nothing was recorded, so the key is renewed.
    // Anything else (InternalError, an AppSync system error such as a Lambda timeout, a network
    // error) may have committed, so the key is kept and a retry finds the recorded movement.
    private static readonly HashSet<string> TypedRefusals =
    [
        "BadRequest", "ProductNotFound", "WarehouseNotFound", "WarehouseInactive", "InsufficientStock",
        "IdempotencyKeyReused", "TransactionConflict", "Unauthorized",
    ];

    [Inject] protected StockAdminService Stock { get; set; } = default!;

    [Parameter, EditorRequired] public string ProductId { get; set; } = string.Empty;

    // Every warehouse, sorted by priority then code.
    [Parameter, EditorRequired] public List<Warehouse> Warehouses { get; set; } = [];

    // The product's balances, one per warehouse holding it.
    [Parameter, EditorRequired] public List<WarehouseStockInfo> Balances { get; set; } = [];

    // Raised with a success message after a movement is recorded.
    [Parameter] public EventCallback<string> OnRecorded { get; set; }

    // Raised after IdempotencyKeyReused, so the page reloads and shows what the earlier attempt did.
    [Parameter] public EventCallback OnPossiblyRecorded { get; set; }

    [Parameter] public EventCallback OnCancel { get; set; }

    protected TModel Model { get; } = new();

    protected bool Busy { get; private set; }

    protected string? Error { get; private set; }

    // (field, message) of the last refusal that names one of this form's fields.
    protected (string Field, string Message)? ServerError { get; private set; }

    // The GraphQL input fields this form shows, e.g. "invoiceNumber".
    protected abstract IReadOnlySet<string> Fields { get; }

    // e.g. "record the receipt", for the failure banner.
    protected abstract string ActionName { get; }

    protected abstract Task<StockMovementResult> SendAsync();

    protected abstract string Describe(StockMovementResult result);

    protected IEnumerable<Warehouse> ActiveWarehouses => Warehouses.Where(w => w.Active);

    // Warehouses with stock to take out; inactive ones included, which is how one is emptied.
    protected IEnumerable<(string Id, string Label)> WarehousesHoldingStock =>
        Balances.Where(b => b.Available > 0).Select(b => (b.WarehouseId, $"{LabelOf(b)} ({b.Available} available)"));

    protected string LabelOf(Warehouse warehouse) =>
        $"{warehouse.Code} — {warehouse.Name}{(warehouse.Active ? "" : " (inactive)")}";

    private string LabelOf(WarehouseStockInfo balance) =>
        Warehouses.FirstOrDefault(w => w.Id == balance.WarehouseId) is { } warehouse
            ? LabelOf(warehouse)
            : balance.WarehouseCode;

    protected async Task SubmitAsync()
    {
        Busy = true;
        Error = null;
        ServerError = null;
        try
        {
            StockMovementResult result;
            try
            {
                result = await SendAsync();
            }
            catch (GraphQLException ex) when (ex.ErrorType == "TransactionConflict")
            {
                // Retryable: the Lambda gave up after its own retries; the same key is safe to resend.
                result = await SendAsync();
            }

            Model.RenewIdempotencyKey();
            await OnRecorded.InvokeAsync(Describe(result));
        }
        catch (GraphQLException ex) when (ex.ErrorType == "IdempotencyKeyReused")
        {
            Model.RenewIdempotencyKey();
            Error = "An earlier attempt of this form, with different values, may already be recorded. "
                + "Check the history below before submitting again.";
            await OnPossiblyRecorded.InvokeAsync();
        }
        catch (GraphQLException ex) when (ex.ErrorType is not null && TypedRefusals.Contains(ex.ErrorType))
        {
            Model.RenewIdempotencyKey();
            var message = ex.ErrorType == "InsufficientStock" && ex.ErrorAvailable is { } available
                ? $"Only {available} available"
                : ex.Message;
            if (ex.ErrorField is not null && Fields.Contains(ex.ErrorField))
                ServerError = (ex.ErrorField, message);
            else
                Error = message;
        }
        catch (Exception ex)
        {
            Error = $"Failed to {ActionName}: {ex.Message}. Submitting this form again is safe: it will not be recorded twice. If you close it, check the history before recording the movement again.";
        }
        finally
        {
            Busy = false;
        }
    }

    protected RenderFragment? ServerMessage(string field) =>
        ServerError is { } error && error.Field == field
            ? builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "validation-message server-error");
                builder.AddAttribute(2, "data-field", field);
                builder.AddContent(3, error.Message);
                builder.CloseElement();
            }
            : null;
}
