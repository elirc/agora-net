using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

internal sealed record TestAccount(HttpClient Client, Guid Id, string Email) : IDisposable
{
    public void Dispose() => Client.Dispose();
}

internal static class AccountTestHelpers
{
    public static async Task<TestAccount> Create(ReportTestScenario scenario, string prefix)
    {
        var client = scenario.App.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid():N}@example.test";
        client.UseBearer(await TestAuth.RegisterAsync(client, email));
        Guid id = default;
        await scenario.Db(async db => id = (await db.Customers.SingleAsync(c => c.Email == email)).Id);
        return new TestAccount(client, id, email);
    }
}
