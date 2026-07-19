namespace DualWriteDemo.Shared;

public sealed record OrderItemEvent(
    Guid ProductId,
    int Quantity,
    decimal UnitPrice);

public sealed record OrderCreatedEvent(
    Guid OrderId,
    string CustomerName,
    List<OrderItemEvent> Items,
    DateTime CreatedAt);
