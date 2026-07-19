namespace DualWriteDemo.InventoryService.Models;

public sealed class Product
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
}
