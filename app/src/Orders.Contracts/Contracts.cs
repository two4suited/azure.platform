namespace Orders.Contracts;

public record Product(string Id, string Name, string Description, decimal Price);

public record OrderLine(string ProductId, int Quantity, decimal UnitPrice);

public record PlaceOrderRequest(string CustomerEmail, IReadOnlyList<OrderLine> Lines);

public record Order(string Id, string CustomerEmail, IReadOnlyList<OrderLine> Lines,
    decimal Total, string Status, DateTimeOffset PlacedAt, DateTimeOffset? ShippedAt = null);

/// <summary>Published by the Orders API to the order-events topic.</summary>
public record OrderPlaced(string OrderId, string CustomerEmail, decimal Total, DateTimeOffset PlacedAt);

/// <summary>Placed on the invoice-requests queue by the fulfillment job.</summary>
public record InvoiceRequest(string OrderId, string CustomerEmail, decimal Total);

public static class Messaging
{
    public const string OrderEventsTopic = "order-events";
    public const string FulfillmentSubscription = "fulfillment";
    public const string AuditSubscription = "audit";
    public const string InvoiceQueue = "invoice-requests";
    public const string AuditTable = "auditlog";
}

public static class CosmosNames
{
    public const string Database = "ordersdb";
    public const string ProductsContainer = "products";
    public const string OrdersContainer = "orders";
}

public static class CosmosJson
{
    /// <summary>camelCase so records serialize with the lowercase "id" property Cosmos requires.</summary>
    public static readonly System.Text.Json.JsonSerializerOptions Options =
        new(System.Text.Json.JsonSerializerDefaults.Web);
}
