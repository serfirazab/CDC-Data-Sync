using System.Text.Json;
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

        // Tek transaction icinde: Order + OrderItems + OutboxMessage birlikte yazilir
        await using var tx = await db.Database.BeginTransactionAsync();

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

        db.Orders.Add(order);

        // Outbox mesaji da ayni transaction icinde
        var orderEvent = new OrderCreatedEvent(
            order.Id,
            order.CustomerName,
            order.Items.Select(i => new OrderItemEvent(i.ProductId, i.Quantity, i.UnitPrice)).ToList(),
            order.CreatedAt);

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "OrderCreated",
            Payload = JsonSerializer.Serialize(orderEvent),
            CreatedAt = DateTime.UtcNow
        };

        db.OutboxMessages.Add(outboxMessage);

        await db.SaveChangesAsync();

        logger.LogInformation("Order {OrderId} and outbox message saved in single transaction", orderId);

        // Fault injection: crash simule edilirse transaction rollback olur
        // Faz 1'den farki: outbox sayesinde hicbir ara durum olusmaz
        var faultEnabled = config.GetValue<bool>("FaultInjection:Enabled");
        if (faultEnabled && request.SimulateCrash)
        {
            logger.LogWarning("Fault injection triggered for order {OrderId}. Rolling back transaction.", orderId);
            await tx.RollbackAsync();
            throw new InvalidOperationException("Simulated crash — transaction rolled back. No data written.");
        }

        // Transaction commit: Order + OutboxMessage birlikte kalici olur
        await tx.CommitAsync();

        logger.LogInformation("Order {OrderId} committed. Debezium will pick up the outbox message.", orderId);

        return Ok(new OrderResponse(order.Id, order.CustomerName, order.TotalAmount, order.Status, order.CreatedAt));
    }
}
