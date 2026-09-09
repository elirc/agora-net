using Agora.Api.Queries;
using Microsoft.AspNetCore.DataProtection;

namespace Agora.Tests.Unit;

public class OrderHistoryCursorTests
{
    [Fact]
    public void Same_key_ring_survives_provider_restart_but_another_application_cannot_read_cursor()
    {
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "agora-cursor-" + Guid.NewGuid().ToString("N")));
        directory.Create();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var owner = Guid.NewGuid();
            var value = new OrderHistoryCursor(1, owner, now.UtcTicks, now.UtcTicks, "ORDER-A", 25, now.AddHours(24).UtcTicks);
            OrderHistoryCursorProtector Create(string application) => new(DataProtectionProvider.Create(directory,
                builder => builder.SetApplicationName(application)));

            var encoded = Create("Agora.OrderHistory").Protect(value);

            Assert.Equal(value, Create("Agora.OrderHistory").Read(encoded, owner, 25, now));
            Assert.Throws<InvalidOrderHistoryCursorException>(() => Create("Another.Application").Read(encoded, owner, 25, now));
            Assert.Throws<InvalidOrderHistoryCursorException>(() => new OrderHistoryCursorProtector(new EphemeralDataProtectionProvider())
                .Read(encoded, owner, 25, now));
        }
        finally
        {
            foreach (var file in directory.GetFiles()) file.Delete();
            directory.Delete();
        }
    }
}
