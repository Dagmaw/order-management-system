namespace OrderManagement.Application.Exceptions;

public sealed class InventoryRejectedException(int statusCode) : Exception("Inventory rejected the operation.")
{
    public int StatusCode { get; } = statusCode;
}

public sealed class InventoryUnavailableException(bool outcomeUnknown)
    : Exception("Inventory did not return a usable response. Reconcile the reservation before deciding its outcome.")
{
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}
