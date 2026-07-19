using System.Text.Json;
using Confluent.Kafka;
using DualWriteDemo.InventoryService.Data;
using DualWriteDemo.Shared;
using Microsoft.EntityFrameworkCore;

namespace DualWriteDemo.InventoryService.Workers;

public sealed class OrderCreatedConsumer(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderCreatedConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = "localhost:9094",
            GroupId = "inventory-service",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
        consumer.Subscribe("order-created");

        logger.LogInformation("Kafka consumer started, listening on topic: order-created");

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
                    if (orderEvent is null)
                    {
                        logger.LogWarning("Deserialized null event, skipping");
                        continue;
                    }

                    logger.LogInformation("Processing OrderCreated event for OrderId: {OrderId}", orderEvent.OrderId);

                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

                    foreach (var item in orderEvent.Items)
                    {
                        var product = await db.Products.FindAsync([item.ProductId], stoppingToken);
                        if (product is null)
                        {
                            logger.LogWarning("Product {ProductId} not found, skipping", item.ProductId);
                            continue;
                        }

                        product.StockQuantity -= item.Quantity;
                        logger.LogInformation("Product {ProductId} stock reduced by {Quantity}. New stock: {Stock}",
                            item.ProductId, item.Quantity, product.StockQuantity);
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    logger.LogError(ex, "Kafka consume error");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error processing event");
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Kafka consumer stopping");
        }
        finally
        {
            consumer.Close();
        }
    }
}
