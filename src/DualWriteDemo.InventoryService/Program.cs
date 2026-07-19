using DualWriteDemo.InventoryService.Data;
using DualWriteDemo.InventoryService.Models;
using DualWriteDemo.InventoryService.Workers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("InventoryDb")));

builder.Services.AddHostedService<OrderCreatedConsumer>();

var app = builder.Build();

// Seed products on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await db.Database.EnsureCreatedAsync();

    if (!await db.Products.AnyAsync())
    {
        var products = new List<Product>
        {
            new() { Id = Guid.Parse("a1b2c3d4-0001-4000-8000-000000000001"), Name = "Laptop", StockQuantity = 50 },
            new() { Id = Guid.Parse("a1b2c3d4-0002-4000-8000-000000000002"), Name = "Mouse", StockQuantity = 200 },
            new() { Id = Guid.Parse("a1b2c3d4-0003-4000-8000-000000000003"), Name = "Keyboard", StockQuantity = 150 },
            new() { Id = Guid.Parse("a1b2c3d4-0004-4000-8000-000000000004"), Name = "Monitor", StockQuantity = 75 },
            new() { Id = Guid.Parse("a1b2c3d4-0005-4000-8000-000000000005"), Name = "Headphone", StockQuantity = 120 },
            new() { Id = Guid.Parse("a1b2c3d4-0006-4000-8000-000000000006"), Name = "Webcam", StockQuantity = 90 },
            new() { Id = Guid.Parse("a1b2c3d4-0007-4000-8000-000000000007"), Name = "USB-C Hub", StockQuantity = 80 },
            new() { Id = Guid.Parse("a1b2c3d4-0008-4000-8000-000000000008"), Name = "SSD 1TB", StockQuantity = 60 },
        };

        db.Products.AddRange(products);
        await db.SaveChangesAsync();
        app.Logger.LogInformation("Seeded {Count} products", products.Count);
    }
}

app.MapGet("/api/products", async (InventoryDbContext db) =>
{
    var products = await db.Products.OrderBy(p => p.Name).ToListAsync();
    return Results.Ok(products);
});

app.Run();
