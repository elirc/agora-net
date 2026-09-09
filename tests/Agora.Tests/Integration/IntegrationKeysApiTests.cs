using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class IntegrationKeysApiTests
{
    [Fact]
    public async Task Keys_follow_explicit_scope_scheme_matrix_and_disclose_secret_only_on_issue()
    {
        using var scenario = await ReportTestScenario.Create(); using var customer = await AccountTestHelpers.Create(scenario, "key-customer");
        var response = await scenario.Admin.PostAsJsonAsync("/api/admin/integration-keys", new CreateIntegrationKeyRequest(" Catalog job ", 30, [" catalogread "]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        var issued = (await response.Content.ReadFromJsonAsync<IntegrationKeyCreatedResponse>())!;
        Assert.Equal("Catalog job", issued.Key.Name); Assert.Equal(new[] { "CatalogRead" }, issued.Key.Scopes); Assert.Equal(76, issued.ApiKey.Length);
        await scenario.Db(async db =>
        {
            var stored = await db.Set<IntegrationApiKey>().SingleAsync();
            Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(issued.ApiKey)), stored.SecretDigest); Assert.Equal(32, stored.SecretDigest.Length);
            Assert.Equal(TimeSpan.FromDays(30), stored.ExpiresAt - stored.CreatedAt);
        });
        using var machine = scenario.App.CreateClient(); machine.DefaultRequestHeaders.Add("X-Agora-Api-Key", issued.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await machine.GetAsync("/api/integrations/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await machine.GetAsync("/api/integrations/inventory")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.PostAsJsonAsync("/api/products", CatalogImportApiTests.Request(Guid.NewGuid(), "key-no-write").Products[0].Product)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.PostAsJsonAsync("/api/admin/integration-keys", new CreateIntegrationKeyRequest("Escalation", 1, ["InventoryRead"]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await scenario.Admin.GetAsync("/api/integrations/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.Client.GetAsync("/api/integrations/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.PostAsJsonAsync("/api/admin/integration-keys", new CreateIntegrationKeyRequest("No", 1, ["CatalogRead"]))).StatusCode);
        using var publicClient = scenario.App.CreateClient(); Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync("/api/products")).StatusCode);
        scenario.Commands.Statements.Clear();
        var listed = await scenario.Admin.GetAsync("/api/admin/integration-keys"); var body = await listed.Content.ReadAsStringAsync();
        Assert.DoesNotContain(issued.ApiKey, body); Assert.DoesNotContain("secretDigest", body); Assert.DoesNotContain("apiKey", body);
        Assert.DoesNotContain(scenario.Commands.Statements, sql => sql.Contains("SecretDigest"));
        Assert.True(listed.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Both_scopes_allow_only_bounded_reads_and_revocation_expiry_wrong_secret_fail_immediately()
    {
        using var scenario = await ReportTestScenario.Create();
        var issued = (await (await scenario.Admin.PostAsJsonAsync("/api/admin/integration-keys", new CreateIntegrationKeyRequest("Sync", 1, ["CatalogRead", "InventoryRead"])))
            .Content.ReadFromJsonAsync<IntegrationKeyCreatedResponse>())!;
        using var machine = scenario.App.CreateClient(); machine.DefaultRequestHeaders.Add("X-Agora-Api-Key", issued.ApiKey);
        var catalog = await machine.GetFromJsonAsync<PagedResult<IntegrationCatalogRow>>("/api/integrations/catalog?pageSize=1"); Assert.Single(catalog!.Items);
        var inventory = await machine.GetFromJsonAsync<PagedResult<IntegrationInventoryRow>>("/api/integrations/inventory?pageSize=1"); Assert.Single(inventory!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await machine.GetAsync("/api/integrations/catalog?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await machine.GetAsync("/api/integrations/inventory?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.GetAsync("/api/admin/integration-keys?pageSize=101")).StatusCode);
        foreach (var invalid in new[] { "bad", Guid.NewGuid().ToString("N") + issued.ApiKey[32..], issued.ApiKey[..33] + new string('z', 43) })
        {
            using var bad = scenario.App.CreateClient(); bad.DefaultRequestHeaders.Add("X-Agora-Api-Key", invalid);
            Assert.Equal(HttpStatusCode.Unauthorized, (await bad.GetAsync("/api/integrations/catalog")).StatusCode);
        }
        (await scenario.Admin.PostAsync($"/api/admin/integration-keys/{issued.Key.Id}/revoke", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.GetAsync("/api/integrations/catalog")).StatusCode);
        var second = (await (await scenario.Admin.PostAsJsonAsync("/api/admin/integration-keys", new CreateIntegrationKeyRequest("Expiry", 1, ["InventoryRead"])))
            .Content.ReadFromJsonAsync<IntegrationKeyCreatedResponse>())!;
        await scenario.Db(async db => { var key = await db.Set<IntegrationApiKey>().SingleAsync(k => k.Id == second.Key.Id); db.Entry(key).Property(k => k.ExpiresAt).CurrentValue = DateTimeOffset.UnixEpoch; await db.SaveChangesAsync(); });
        using var expired = scenario.App.CreateClient(); expired.DefaultRequestHeaders.Add("X-Agora-Api-Key", second.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await expired.GetAsync("/api/integrations/inventory")).StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("1")]
    [InlineData("CatalogRead,InventoryRead")]
    public async Task Unsupported_scopes_cannot_create_metadata(string scope)
    {
        using var scenario = await ReportTestScenario.Create();
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync("/api/admin/integration-keys", new CreateIntegrationKeyRequest("Invalid", 1, [scope]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync("/api/admin/integration-keys", new CreateIntegrationKeyRequest("Duplicate", 1, ["CatalogRead", " catalogread "]))).StatusCode);
        await scenario.Db(async db => Assert.Empty(await db.Set<IntegrationApiKey>().ToListAsync()));
    }
}
