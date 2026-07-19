using System.Text.Json;
using Confluent.Kafka;
using DualWriteDemo.OrderService.Data;
using DualWriteDemo.OrderService.Models;
using DualWriteDemo.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DualWriteDemo.OrderService.Controllers;

public sealed record CreateOrderRequest(
    string CustomerName,
    List<OrderItemRequest> Items,
    bool SimulateCrash = false);

public sealed record OrderItemRequest(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record OrderResponse(
    Guid Id, string CustomerName, decimal TotalAmount, string Status, DateTime CreatedAt);

[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController(
    OrderDbContext db,
    IConfiguration config,
    ILogger<OrdersController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<OrderResponse>>> GetAll()
    {
        var orders = await db.Orders
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderResponse(o.Id, o.CustomerName, o.TotalAmount, o.Status, o.CreatedAt))
            .ToListAsync();

        return Ok(orders);
    }

    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create([FromBody] CreateOrderRequest request)
    {
        var orderId = Guid.NewGuid();
        var totalAmount = request.Items.Sum(i => i.Quantity * i.UnitPrice);

        var order = new Order
        {
            Id = orderId,
            CustomerName = request.CustomerName,
            TotalAmount = totalAmount,
            Status = "Created",
            CreatedAt = DateTime.UtcNow,
            Items = request.Items.Select(i => new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        // Adim 1: DB'ye yaz (tek local transaction)
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        logger.LogInformation("Order {OrderId} saved to database", orderId);

        // Fault injection: fault injection acik ve simulateCrash true ise patla
        var faultEnabled = config.GetValue<bool>("FaultInjection:Enabled");
        if (faultEnabled && request.SimulateCrash)
        {
            logger.LogWarning("Fault injection triggered for order {OrderId}. Crashing after DB write.", orderId);
            throw new InvalidOperationException("Simulated crash after DB write — event never published.");
        }

        // Adim 2: Kafka'ya publish (ayri, atomik olmayan adim)
        await PublishOrderCreatedEvent(order);

        logger.LogInformation("Order {OrderId} event published to Kafka", orderId);

        return Ok(new OrderResponse(order.Id, order.CustomerName, order.TotalAmount, order.Status, order.CreatedAt));
    }

    private async Task PublishOrderCreatedEvent(Order order)
    {
        var kafkaConfig = config.GetSection("Kafka");
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = kafkaConfig["BootstrapServers"] ?? "localhost:9094",
            Acks = Acks.All
        };

        var orderEvent = new OrderCreatedEvent(
            order.Id,
            order.CustomerName,
            order.Items.Select(i => new OrderItemEvent(i.ProductId, i.Quantity, i.UnitPrice)).ToList(),
            order.CreatedAt);

        var payload = JsonSerializer.Serialize(orderEvent);

        using var producer = new ProducerBuilder<Null, string>(producerConfig).Build();
        await producer.ProduceAsync("order-created", new Message<Null, string> { Value = payload });
    }
}
