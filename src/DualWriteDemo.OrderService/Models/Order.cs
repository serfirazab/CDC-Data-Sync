namespace DualWriteDemo.OrderService.Models;

public sealed class Order
{
    public Guid Id { get; init; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Created";
    public DateTime CreatedAt { get; init; }

    public List<OrderItem> Items { get; init; } = [];
}
