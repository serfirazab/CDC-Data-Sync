namespace DualWriteDemo.OrderService.Models;

public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? ProcessedAt { get; set; }
}
