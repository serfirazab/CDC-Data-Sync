namespace DualWriteDemo.Shared;

public sealed record OrderItemEvent(
    Guid ProductId,
    int Quantity,
    decimal UnitPrice);

public sealed record OrderCreatedEvent(
    Guid OrderId,
    string CustomerName,
    IReadOnlyList<OrderItemEvent> Items,
    DateTime CreatedAt);
