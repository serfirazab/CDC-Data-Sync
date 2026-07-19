namespace DualWriteDemo.OrderService.Models;

public sealed class OrderItem
{
    public Guid Id { get; init; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }

    public Order Order { get; init; } = null!;
}
