using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public sealed class GuestOrderAccessPersistenceTests
{
    [Fact]
    public async Task Token_is_digest_only_order_bound_exactly_expiring_and_rotation_invalidates_old_value()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite(connection).Options;
        await using var db = new AgoraDbContext(options); await db.Database.EnsureCreatedAsync();
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        var service = new GuestOrderAccessService(db, clock);
        var firstOrder = Guest("ORD-GUEST-A"); var secondOrder = Guest("ORD-GUEST-B");
        db.Orders.AddRange(firstOrder, secondOrder); await db.SaveChangesAsync();

        var issued = service.Issue(firstOrder); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var stored = await db.Set<GuestOrderCredential>().SingleAsync();
        Assert.Equal(32, stored.SecretDigest.Length);
        Assert.DoesNotContain(issued.Token, Convert.ToHexString(stored.SecretDigest), StringComparison.Ordinal);
        await service.EnsureCanReadAsync(firstOrder, new OrderAccessActor(null, false, issued.Token), default);
        await Assert.ThrowsAsync<NotFoundException>(() => service.EnsureCanReadAsync(secondOrder,
            new OrderAccessActor(null, false, issued.Token), default));

        clock.Instant = issued.Credential.ExpiresAt;
        await Assert.ThrowsAsync<NotFoundException>(() => service.EnsureCanReadAsync(firstOrder,
            new OrderAccessActor(null, false, issued.Token), default));
        clock.Instant = issued.Credential.IssuedAt.AddDays(1);
        var replacement = await service.RotateAsync(firstOrder, Guid.NewGuid(), default); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => service.EnsureCanReadAsync(firstOrder,
            new OrderAccessActor(null, false, issued.Token), default));
        await service.EnsureCanReadAsync(firstOrder, new OrderAccessActor(null, false, replacement.Token), default);
    }

    [Fact]
    public async Task Account_order_requires_owner_or_admin_even_when_email_or_guest_token_matches()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite(connection).Options;
        await using var db = new AgoraDbContext(options); await db.Database.EnsureCreatedAsync();
        var customer = new Customer { Email = "owner@example.test", PasswordHash = "hash" };
        var owner = customer.Id; var order = Guest("ORD-ACCOUNT"); order.CustomerId = owner;
        db.AddRange(customer, order); await db.SaveChangesAsync();
        var service = new GuestOrderAccessService(db, TimeProvider.System);

        await service.EnsureCanReadAsync(order, new OrderAccessActor(owner, false, null), default);
        await service.EnsureCanReadAsync(order, new OrderAccessActor(null, true, null), default);
        await Assert.ThrowsAsync<NotFoundException>(() => service.EnsureCanReadAsync(order,
            new OrderAccessActor(null, false, "email-is-not-authority"), default));
    }

    private static Order Guest(string number) => new()
    {
        Number = number, Email = "same@example.test", ShippingAddress = new Address
        { FullName = "Guest", Line1 = "1 Main", City = "X", Region = "X", PostalCode = "1", Country = "US" }
    };

    private sealed class MutableClock(DateTimeOffset instant) : TimeProvider
    { public DateTimeOffset Instant { get; set; } = instant; public override DateTimeOffset GetUtcNow() => Instant; }
}
