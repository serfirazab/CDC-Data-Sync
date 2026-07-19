using System.Collections.Concurrent;
using System.Text.Json;
using Confluent.Kafka;
using DualWriteDemo.Shared;

namespace DualWriteDemo.Dashboard.Services;

public sealed class EventLogService(ILogger<EventLogService> logger) : BackgroundService
{
    private readonly ConcurrentQueue<EventLogEntry> _events = new();
    private readonly int _maxEvents = 500;

    public IReadOnlyCollection<EventLogEntry> GetRecentEvents()
    {
        return _events.Reverse().Take(100).ToList();
    }

    public bool ContainsOrderId(Guid orderId)
    {
        return _events.Any(e => e.OrderId == orderId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = "localhost:9094",
            GroupId = "dashboard-event-log",
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        consumer.Subscribe("order-created");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);

                    // Debezium Outbox SMT payload'ı JSON string olarak publish eder.
                    // Kafka mesajı bazen çift encode edilir ("{\"Items\":...}").
                    var messageValue = result.Message.Value;
                    if (messageValue.StartsWith('"'))
                    {
                        messageValue = JsonSerializer.Deserialize<string>(messageValue) ?? messageValue;
                    }
                    var orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(messageValue);

                    if (orderEvent is not null)
                    {
                        var entry = new EventLogEntry(
                            orderEvent.OrderId,
                            "OrderCreated",
                            orderEvent.CustomerName,
                            orderEvent.Items.Count,
                            result.Message.Timestamp.UtcDateTime);

                        _events.Enqueue(entry);

                        while (_events.Count > _maxEvents)
                            _events.TryDequeue(out _);
                    }
                }
                catch (ConsumeException)
                {
                    // Ignore consumer errors in dashboard
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error processing event");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            consumer.Close();
        }
    }
}

public sealed record EventLogEntry(
    Guid OrderId,
    string EventType,
    string CustomerName,
    int ItemCount,
    DateTime ReceivedAt);
