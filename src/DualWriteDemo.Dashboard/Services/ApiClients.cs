using System.Text.Json;

namespace DualWriteDemo.Dashboard.Services;

public sealed record OrderSummary(Guid Id, string CustomerName, decimal TotalAmount, string Status, DateTime CreatedAt);

public sealed record CreateOrderRequest(string CustomerName, List<OrderItemInput> Items, bool SimulateCrash);

public sealed record OrderItemInput(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record ProductInfo(Guid Id, string Name, int StockQuantity);

public sealed class OrderApiClient(HttpClient http)
{
    public async Task<List<OrderSummary>> GetOrdersAsync()
    {
        var response = await http.GetAsync("api/orders");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<OrderSummary>>(json) ?? [];
    }

    public async Task<OrderSummary?> CreateOrderAsync(CreateOrderRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await http.PostAsync("api/orders", content);

        if (!response.IsSuccessStatusCode) return null;

        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<OrderSummary>(responseJson);
    }
}

public sealed class InventoryApiClient(HttpClient http)
{
    public async Task<List<ProductInfo>> GetProductsAsync()
    {
        var response = await http.GetAsync("api/products");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<ProductInfo>>(json) ?? [];
    }
}
