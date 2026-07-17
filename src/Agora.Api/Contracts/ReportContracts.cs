namespace Agora.Api.Contracts;

public sealed record SalesBucketResponse(
    string Period,
    int OrderCount,
    int ItemsSold,
    decimal GrossRevenue);

public sealed record SalesReportResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string Interval,
    int TotalOrders,
    decimal TotalRevenue,
    IReadOnlyList<SalesBucketResponse> Buckets);

public sealed record TopProductResponse(
    string Sku,
    string ProductName,
    int UnitsSold,
    decimal Revenue);

public sealed record LowStockResponse(
    string Sku,
    string ProductName,
    string VariantName,
    int QuantityOnHand,
    int QuantityReserved,
    int QuantityAvailable);

public sealed record DiscountUsageResponse(
    string Code,
    string Type,
    int TimesUsed,
    int OrderCount,
    decimal TotalDiscounted,
    decimal TotalRevenue);
