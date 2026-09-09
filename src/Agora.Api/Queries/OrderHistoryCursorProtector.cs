using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Agora.Api.Queries;

public sealed record OrderHistoryCursor(int Version, Guid CustomerId, long CutoffTicks, long LastCreatedTicks,
    string LastNumber, int Limit, long ExpiresTicks);

public sealed class InvalidOrderHistoryCursorException : Exception;

public sealed class OrderHistoryCursorProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Agora.OrderHistoryFeed.Cursor.v1");

    public string Protect(OrderHistoryCursor cursor) => _protector.Protect(JsonSerializer.Serialize(cursor));
    public OrderHistoryCursor Read(string encoded, Guid owner, int limit, DateTimeOffset now)
    {
        try
        {
            if (encoded.Length is < 1 or > 4096) throw new InvalidOrderHistoryCursorException();
            var cursor = JsonSerializer.Deserialize<OrderHistoryCursor>(_protector.Unprotect(encoded));
            if (cursor is null || cursor.Version != 1 || cursor.CustomerId != owner || cursor.Limit != limit
                || cursor.ExpiresTicks <= now.UtcTicks || cursor.CutoffTicks < DateTimeOffset.MinValue.UtcTicks
                || cursor.CutoffTicks > DateTimeOffset.MaxValue.UtcTicks || cursor.LastCreatedTicks < DateTimeOffset.MinValue.UtcTicks
                || cursor.LastCreatedTicks > cursor.CutoffTicks || string.IsNullOrEmpty(cursor.LastNumber) || cursor.LastNumber.Length > 64)
                throw new InvalidOrderHistoryCursorException();
            return cursor;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException or FormatException)
        {
            throw new InvalidOrderHistoryCursorException();
        }
    }
}
