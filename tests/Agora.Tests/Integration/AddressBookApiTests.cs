using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class AddressBookApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly AddressDto Home = new(
        "Ada Lovelace", "1 Analytical Way", null, "London", "LDN", "EC1A 1AA", "GB");
    private static readonly AddressDto Office = new(
        "Ada Lovelace", "42 Engine House", "Floor 3", "London", "LDN", "EC2A 2BB", "GB");

    [Fact]
    public async Task Addresses_Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/api/me/addresses");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FirstAddress_BecomesDefault()
    {
        var client = await NewCustomer("first-default@example.com");

        var response = await client.PostAsJsonAsync("/api/me/addresses",
            new SaveAddressRequest("Home", Home, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CustomerAddressResponse>();
        Assert.True(created!.IsDefault);
        Assert.Equal("Home", created.Label);
        Assert.Equal("1 Analytical Way", created.Address.Line1);
    }

    [Fact]
    public async Task SettingNewDefault_ClearsPreviousDefault()
    {
        var client = await NewCustomer("swap-default@example.com");
        await client.PostAsJsonAsync("/api/me/addresses", new SaveAddressRequest("Home", Home, null));

        var second = await client.PostAsJsonAsync("/api/me/addresses",
            new SaveAddressRequest("Office", Office, true));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var addresses = await client.GetFromJsonAsync<List<CustomerAddressResponse>>("/api/me/addresses");
        Assert.Equal(2, addresses!.Count);
        Assert.Single(addresses, a => a.IsDefault);
        Assert.True(addresses.Single(a => a.Label == "Office").IsDefault);
    }

    [Fact]
    public async Task SetDefaultEndpoint_SwitchesDefault()
    {
        var client = await NewCustomer("set-default@example.com");
        await client.PostAsJsonAsync("/api/me/addresses", new SaveAddressRequest("Home", Home, null));
        var office = await (await client.PostAsJsonAsync("/api/me/addresses",
            new SaveAddressRequest("Office", Office, null))).Content
            .ReadFromJsonAsync<CustomerAddressResponse>();

        var response = await client.PostAsync($"/api/me/addresses/{office!.Id}/default", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var addresses = await client.GetFromJsonAsync<List<CustomerAddressResponse>>("/api/me/addresses");
        Assert.True(addresses!.Single(a => a.Label == "Office").IsDefault);
        Assert.False(addresses.Single(a => a.Label == "Home").IsDefault);
    }

    [Fact]
    public async Task Update_ChangesFields()
    {
        var client = await NewCustomer("update-addr@example.com");
        var created = await (await client.PostAsJsonAsync("/api/me/addresses",
            new SaveAddressRequest("Home", Home, null))).Content
            .ReadFromJsonAsync<CustomerAddressResponse>();

        var response = await client.PutAsJsonAsync($"/api/me/addresses/{created!.Id}",
            new SaveAddressRequest("Moved", Office, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CustomerAddressResponse>();
        Assert.Equal("Moved", updated!.Label);
        Assert.Equal("42 Engine House", updated.Address.Line1);
        Assert.True(updated.IsDefault); // unchanged
    }

    [Fact]
    public async Task Delete_RemovesAddress()
    {
        var client = await NewCustomer("delete-addr@example.com");
        var created = await (await client.PostAsJsonAsync("/api/me/addresses",
            new SaveAddressRequest("Home", Home, null))).Content
            .ReadFromJsonAsync<CustomerAddressResponse>();

        var response = await client.DeleteAsync($"/api/me/addresses/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var addresses = await client.GetFromJsonAsync<List<CustomerAddressResponse>>("/api/me/addresses");
        Assert.Empty(addresses!);
    }

    [Fact]
    public async Task Addresses_AreIsolatedBetweenCustomers()
    {
        var alice = await NewCustomer("alice-iso@example.com");
        var created = await (await alice.PostAsJsonAsync("/api/me/addresses",
            new SaveAddressRequest("Home", Home, null))).Content
            .ReadFromJsonAsync<CustomerAddressResponse>();

        var bob = await NewCustomer("bob-iso@example.com");
        var list = await bob.GetFromJsonAsync<List<CustomerAddressResponse>>("/api/me/addresses");
        var update = await bob.PutAsJsonAsync($"/api/me/addresses/{created!.Id}",
            new SaveAddressRequest("Hijack", Office, null));
        var delete = await bob.DeleteAsync($"/api/me/addresses/{created.Id}");

        Assert.Empty(list!);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    private async Task<HttpClient> NewCustomer(string email)
    {
        var client = _factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, email));
        return client;
    }
}
